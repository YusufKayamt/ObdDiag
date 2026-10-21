using ObdDiag.Core.Models;

namespace ObdDiag.Reporting;

/// <summary>Raporun başlığında görünen işletme bilgileri (Ayarlar ekranından girilir).</summary>
/// <param name="Name">İşletme adı</param>
/// <param name="Address">Adres</param>
/// <param name="Phone">Telefon</param>
/// <param name="Email">E-posta</param>
/// <param name="Logo">Logo resminin içeriği (PNG/JPG). Yoksa null.</param>
public sealed record WorkshopInfo(string Name, string? Address = null, string? Phone = null, string? Email = null, byte[]? Logo = null);

/// <summary>Rapordaki bir arıza kodu satırı.</summary>
/// <param name="Code">Örn. "P0104"</param>
/// <param name="Description">Türkçe açıklama. Bilinmiyorsa null.</param>
/// <param name="WasCleared">Kod bu işlem sırasında araçtan silindi mi?</param>
public sealed record ReportFault(string Code, string? Description, bool WasCleared);

/// <summary>
/// Arıza raporunun içeriği. Rapor kütüphanesi veritabanını tanımaz;
/// uygulama bu nesneyi doldurur, <see cref="IReportGenerator"/> PDF'e çevirir.
/// </summary>
public sealed class DiagnosticReport
{
    public required WorkshopInfo Workshop { get; init; }

    /// <summary>Raporun numarası (örn. tamir kaydının numarası).</summary>
    public string? ReportNumber { get; init; }

    public DateTime Date { get; init; } = DateTime.Now;

    // ---- Müşteri ----
    public required string CustomerName { get; init; }
    public string? CustomerPhone { get; init; }

    // ---- Araç ----
    public required string Vin { get; init; }
    public string? Plate { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public int? Mileage { get; init; }

    // ---- Teşhis ----
    public IReadOnlyList<ReportFault> Faults { get; init; } = Array.Empty<ReportFault>();

    /// <summary>Teşhis sırasındaki canlı veri. Alınmadıysa null.</summary>
    public LiveData? LiveData { get; init; }

    /// <summary>Yapılan işlemin açıklaması.</summary>
    public string WorkPerformed { get; init; } = string.Empty;
}
