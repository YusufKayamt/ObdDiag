namespace ObdDiag.App.Services;

/// <summary>İşletmenin raporda görünen bilgileri (Ayarlar ekranından girilir).</summary>
public sealed record WorkshopSettings
{
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>Logo resminin içeriği. Logo seçilmediyse null.</summary>
    public byte[]? Logo { get; init; }
}

/// <summary>İşletme ayarlarını diskte saklar.</summary>
public interface ISettingsStore
{
    /// <summary>Kayıtlı ayarları okur. Hiç kaydedilmemişse veya dosya bozuksa boş ayarlar döner.</summary>
    WorkshopSettings Load();

    void Save(WorkshopSettings settings);
}
