using System.Globalization;
using System.IO;
using System.Windows;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Data;
using FgScanner.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FgScanner.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FGScanner", "logs");
        Directory.CreateDirectory(logDir);

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog((_, config) => config
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(
                    Path.Combine(logDir, "app-.log"),
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14))
            .ConfigureServices(services =>
            {
                if (e.Args.Contains("--fake-scanner"))
                {
                    services.AddSingleton<IScanService>(
                        new FakeScanService { PageDelay = TimeSpan.FromMilliseconds(300) });
                }
                else
                {
                    services.AddSingleton<IScanService, Naps2ScanService>();
                }

                services.AddSingleton<ScanSessionService>();
                services.AddDbContextFactory<FgScannerDbContext>(o =>
                    o.UseSqlite($"Data Source={DbBootstrapper.DefaultDbPath}"));
                services.AddSingleton<GroupService>();
                services.AddSingleton<ProfileService>();
                services.AddSingleton(sp => new FgScanner.Core.Index.IndexExporter());
                services.AddSingleton<IndexingService>();
                services.AddSingleton(sp => new TrashService(
                    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<FgScannerDbContext>>(),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "FGScanner", "trash")));
                services.AddSingleton<ReorderService>();
                services.AddSingleton<AppSettingsService>();
                services.AddSingleton<OcrQueueService>();
                services.AddSingleton<AiQueueService>();
                services.AddSingleton(sp => new FgScanner.Ai.CredentialStore());
                services.AddSingleton<AiWorker>();
                services.AddSingleton(sp => new FgScanner.Scanning.Import.FileImportService());
                services.AddSingleton<FgScanner.Core.IPdfRenderer>(sp => new Naps2PdfRenderer(
                    sp.GetRequiredService<FgScanner.Scanning.Import.FileImportService>()));
                services.AddSingleton(sp => new RetroProcessService(
                    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<FgScannerDbContext>>(),
                    sp.GetRequiredService<GroupService>(),
                    sp.GetRequiredService<TrashService>(),
                    sp.GetRequiredService<FgScanner.Core.IPdfRenderer>()));
                services.AddSingleton(sp => new PageEditingToolset(
                    new FgScanner.Scanning.Editing.ImageEditor(),
                    new FgScanner.Scanning.Export.PdfExportService(),
                    new FgScanner.Scanning.Export.ImageExportService(),
                    sp.GetRequiredService<FgScanner.Scanning.Import.FileImportService>(),
                    sp.GetRequiredService<ReorderService>(),
                    sp.GetRequiredService<OcrQueueService>(),
                    sp.GetRequiredService<AiQueueService>(),
                    sp.GetRequiredService<RetroProcessService>(),
                    sp.GetRequiredService<FgScanner.Ai.CredentialStore>(),
                    sp.GetRequiredService<AppSettingsService>()));
                services.AddSingleton(sp => new FgScanner.Ocr.LanguageManager());
                services.AddSingleton(sp => new FgScanner.Ocr.TesseractRunner(
                    tessdataDir: sp.GetRequiredService<FgScanner.Ocr.LanguageManager>().TessdataDir));
                services.AddSingleton(sp => new FgScanner.Ocr.OcrPipeline(
                    sp.GetRequiredService<FgScanner.Ocr.TesseractRunner>()));
                services.AddSingleton<ProfileOcrTrigger>();
                services.AddSingleton<OcrWorker>();
                services.AddSingleton<ActiveGroupStore>();
                services.AddSingleton<GroupsViewModel>();
                services.AddSingleton<TrashViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<ScanViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ShellWindow>();
            })
            .Build();

        _host.Start();
        Log.Information("FG Scanner starting");

        var appVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        IndexingService.AppVersion = appVersion;
        var backup = DbBootstrapper.MigrateWithBackup(DbBootstrapper.DefaultDbPath, appVersion);
        if (backup is not null)
        {
            Log.Information("Database migrated; pre-migration backup at {Backup}", backup);
        }

        OfferCrashRecovery(_host.Services.GetRequiredService<ScanSessionService>());

        // Bundled English lands in the writable tessdata dir; then the durable queue drains.
        _host.Services.GetRequiredService<FgScanner.Ocr.LanguageManager>().EnsureBundledEnglish();
        _host.Services.GetRequiredService<OcrWorker>().Start();
        _host.Services.GetRequiredService<AiWorker>().Start();

        MainWindow = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow.Show();

        // Background purge of expired trash items (30-day default, configurable).
        var trash = _host.Services.GetRequiredService<TrashService>();
        _ = Task.Run(async () =>
        {
            try
            {
                var purged = await trash.PurgeExpiredAsync();
                if (purged > 0)
                {
                    Log.Information("Purged {Count} expired trash item(s)", purged);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Trash purge failed");
            }
        });
    }

    /// <summary>If a previous instance died mid-scan, offer to pull its pages into this session.</summary>
    private static void OfferCrashRecovery(ScanSessionService sessionService)
    {
        foreach (var orphan in sessionService.FindOrphanedSessions())
        {
            var answer = MessageBox.Show(
                $"A previous session ended unexpectedly with {orphan.Pages.Count} scanned page(s).\n\n" +
                "Recover these pages?",
                "FG Scanner — Recovery",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                sessionService.RecoverInto(orphan);
            }
            else
            {
                ScanSessionService.Discard(orphan);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("FG Scanner exiting");
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
