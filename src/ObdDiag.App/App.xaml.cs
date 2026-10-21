using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Core.Interfaces;
using ObdDiag.Data;
using ObdDiag.Hardware;
using ObdDiag.Reporting;

namespace ObdDiag.App;

public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Veritabanı dosyasının yeri: C:\Users\&lt;kullanıcı&gt;\AppData\Local\ObdDiag\obddiag.db
    /// (Deneme için OBDDIAG_DB_PATH ortam değişkeniyle başka bir dosya gösterilebilir.)
    /// </summary>
    public static string DatabasePath { get; } =
        Environment.GetEnvironmentVariable("OBDDIAG_DB_PATH") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObdDiag", "obddiag.db");

    /// <summary>Ayar dosyasının (settings.json) ve logonun saklandığı klasör.</summary>
    public static string SettingsFolder => Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Bağımlılık enjeksiyonu (DI): sınıflar ihtiyaç duydukları parçaları buradan alır.
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddObdData(DatabasePath);
                services.AddSingleton<IComPortProvider, SerialPortProvider>();
                services.AddSingleton<IObdServiceFactory, ObdServiceFactory>();
                services.AddSingleton<IDialogService, WpfDialogService>();
                // Ayarlar (işletme adı, logo) veritabanı dosyasıyla aynı klasörde saklanır.
                services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(SettingsFolder));
                services.AddSingleton<IReportGenerator, PdfReportGenerator>();
                services.AddSingleton<IReportService, ReportService>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<CustomersViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        try
        {
            _host.Services.EnsureObdDatabaseCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Veritabanı dosyası açılamadı:\n{DatabasePath}\n\n{ex.Message}",
                "ObdDiag - Veritabanı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
