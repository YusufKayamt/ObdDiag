using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObdDiag.App.Services;
using ObdDiag.Core.Models;
using ObdDiag.Reporting;

namespace ObdDiag.App.ViewModels;

/// <summary>"Ayarlar" ekranının beyni: raporda görünen işletme adı, adresi, telefonu ve logosu.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Logo dosyası en fazla bu kadar büyük olabilir (rapor dosyası şişmesin).</summary>
    public const int MaxLogoBytes = 5 * 1024 * 1024;

    private const string ImageFilter = "Resim dosyaları (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";

    private readonly ISettingsStore _store;
    private readonly IReportGenerator _generator;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(ISettingsStore store, IReportGenerator generator, IDialogService dialogs)
    {
        _store = store;
        _generator = generator;
        _dialogs = dialogs;

        var saved = _store.Load();
        _workshopName = saved.Name;
        _address = saved.Address ?? string.Empty;
        _phone = saved.Phone ?? string.Empty;
        _email = saved.Email ?? string.Empty;
        _logo = saved.Logo;
    }

    /// <summary>Örnek raporun yazılacağı yer. Testlerde değiştirilebilir.</summary>
    public string PreviewPath { get; set; } = Path.Combine(Path.GetTempPath(), "ObdDiag_ornek_rapor.pdf");

    [ObservableProperty] private string _workshopName;
    [ObservableProperty] private string _address;
    [ObservableProperty] private string _phone;
    [ObservableProperty] private string _email;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    [NotifyCanExecuteChangedFor(nameof(RemoveLogoCommand))]
    private byte[]? _logo;

    public bool HasLogo => Logo is { Length: > 0 };

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    // ================= Komutlar =================

    [RelayCommand]
    private void ChooseLogo()
    {
        var path = _dialogs.AskOpenFile("Logo Seç", ImageFilter);
        if (path is null) return;

        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxLogoBytes)
            {
                SetMessage("Logo dosyası çok büyük (en fazla 5 MB). Daha küçük bir resim seçin.", isError: true);
                return;
            }
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage($"Logo dosyası okunamadı: {ex.Message}", isError: true);
            return;
        }

        if (!_generator.IsSupportedImage(bytes))
        {
            SetMessage("Seçilen dosya bir resim değil veya biçimi desteklenmiyor. PNG ya da JPG seçin.", isError: true);
            return;
        }

        Logo = bytes;
        SetMessage("Logo seçildi. Kalıcı olması için \"Ayarları Kaydet\"e basın.");
    }

    [RelayCommand(CanExecute = nameof(HasLogo))]
    private void RemoveLogo()
    {
        Logo = null;
        SetMessage("Logo kaldırıldı. Kalıcı olması için \"Ayarları Kaydet\"e basın.");
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(WorkshopName))
        {
            SetMessage("İşletme adı boş olamaz; raporun başlığında bu ad görünür.", isError: true);
            return;
        }
        if (!string.IsNullOrWhiteSpace(Email) && !Email.Contains('@'))
        {
            SetMessage("E-posta adresi geçerli görünmüyor (@ işareti yok).", isError: true);
            return;
        }

        try
        {
            _store.Save(Current());
            SetMessage("Ayarlar kaydedildi. Bundan sonraki raporlarda bu bilgiler kullanılacak.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMessage($"Ayarlar kaydedilemedi: {ex.Message}", isError: true);
        }
    }

    /// <summary>Ekrandaki (henüz kaydedilmemiş olsa bile) bilgilerle örnek bir rapor üretip açar.</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        var report = new DiagnosticReport
        {
            Workshop = ToWorkshopInfo(Current()),
            ReportNumber = "ÖRNEK",
            Date = DateTime.Now,
            CustomerName = "Örnek Müşteri",
            CustomerPhone = "0500 000 00 00",
            Vin = "WVWZZZ1JZXW000001",
            Plate = "23 ABC 123",
            Make = "Volkswagen",
            Model = "Golf",
            Year = 2015,
            Mileage = 150_000,
            Faults = new[]
            {
                new ReportFault("P0104", null, WasCleared: true),
                new ReportFault("P0301", null, WasCleared: false)
            },
            LiveData = new LiveData(800, 0, 88, 2),
            WorkPerformed = "Bu bir örnek rapordur. Logonun ve işletme bilgilerinin nasıl göründüğünü kontrol etmek içindir."
        };

        try
        {
            var bytes = await Task.Run(() => _generator.GeneratePdf(report));
            await File.WriteAllBytesAsync(PreviewPath, bytes);
            _dialogs.OpenFile(PreviewPath);
            SetMessage("Örnek rapor açıldı.");
        }
        catch (IOException)
        {
            SetMessage("Örnek rapor yazılamadı. Önceki örnek rapor açıksa kapatıp tekrar deneyin.", isError: true);
        }
        catch (Exception ex)
        {
            SetMessage($"Örnek rapor oluşturulamadı: {ex.Message}", isError: true);
        }
    }

    // ================= Yardımcılar =================

    private WorkshopSettings Current() => new()
    {
        Name = WorkshopName.Trim(),
        Address = Clean(Address),
        Phone = Clean(Phone),
        Email = Clean(Email),
        Logo = Logo
    };

    private static WorkshopInfo ToWorkshopInfo(WorkshopSettings s) => new(s.Name, s.Address, s.Phone, s.Email, s.Logo);

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void SetMessage(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }
}
