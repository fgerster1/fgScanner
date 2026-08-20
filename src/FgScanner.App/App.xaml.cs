using System.Globalization;
using System.IO;
using System.Windows;
using FgScanner.App.Views;
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
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<ShellWindow>();
            })
            .Build();

        _host.Start();
        Log.Information("FG Scanner starting");

        MainWindow = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow.Show();
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
