using System.Globalization;
using System.IO;
using System.Windows;
using FgScanner.App.Services;
using FgScanner.App.Views;
using FgScanner.Scanning;
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
                services.AddSingleton<ScanViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ShellWindow>();
            })
            .Build();

        _host.Start();
        Log.Information("FG Scanner starting");

        OfferCrashRecovery(_host.Services.GetRequiredService<ScanSessionService>());

        MainWindow = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow.Show();
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
