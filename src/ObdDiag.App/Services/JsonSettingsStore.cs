using System.IO;
using System.Text.Json;

namespace ObdDiag.App.Services;

/// <summary>
/// Ayarları bir klasörde saklar: yazılar "settings.json" dosyasına, logo "logo.img" dosyasına.
/// Logonun kopyası alınır; böylece kullanıcı orijinal resmi silse/taşısa da rapor bozulmaz.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string SettingsFileName = "settings.json";
    private const string LogoFileName = "logo.img";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;

    public JsonSettingsStore(string folder)
    {
        _folder = folder;
    }

    private string SettingsPath => Path.Combine(_folder, SettingsFileName);
    private string LogoPath => Path.Combine(_folder, LogoFileName);

    public WorkshopSettings Load()
    {
        WorkshopSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath))?.ToSettings() ?? new WorkshopSettings()
                : new WorkshopSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Bozuk ayar dosyası programı açılmaz hale getirmesin; boş ayarlarla devam edilir.
            settings = new WorkshopSettings();
        }

        try
        {
            if (File.Exists(LogoPath))
                settings = settings with { Logo = File.ReadAllBytes(LogoPath) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logo okunamazsa rapor logosuz çıkar.
        }

        return settings;
    }

    public void Save(WorkshopSettings settings)
    {
        Directory.CreateDirectory(_folder);

        var stored = new StoredSettings(settings.Name, settings.Address, settings.Phone, settings.Email);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored, JsonOptions));

        if (settings.Logo is { Length: > 0 } logo)
            File.WriteAllBytes(LogoPath, logo);
        else if (File.Exists(LogoPath))
            File.Delete(LogoPath);
    }

    /// <summary>JSON dosyasına yazılan kısım (logo ayrı dosyada durur).</summary>
    private sealed record StoredSettings(string? Name, string? Address, string? Phone, string? Email)
    {
        public WorkshopSettings ToSettings() => new()
        {
            Name = Name ?? string.Empty,
            Address = Address,
            Phone = Phone,
            Email = Email
        };
    }
}
