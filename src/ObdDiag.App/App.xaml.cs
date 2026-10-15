using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Core.Interfaces;
using ObdDiag.Hardware;

namespace ObdDiag.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Bağımlılık enjeksiyonu (DI): sınıflar ihtiyaç duydukları parçaları buradan alır.
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IComPortProvider, SerialPortProvider>();
                services.AddSingleton<IObdServiceFactory, ObdServiceFactory>();
                services.AddSingleton<IDialogService, WpfDialogService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
