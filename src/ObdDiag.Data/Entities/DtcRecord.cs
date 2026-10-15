namespace ObdDiag.Data.Entities;

/// <summary>Bir tamir kaydında bulunan arıza kodu.</summary>
public class DtcRecord
{
    public int Id { get; set; }
    public int RepairRecordId { get; set; }
    public RepairRecord? RepairRecord { get; set; }

    /// <summary>Örn. "P0104".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Kodun Türkçe açıklaması (8. adımda doldurulacak).</summary>
    public string? Description { get; set; }

    /// <summary>Kod bu işlem sırasında araçtan silindi mi?</summary>
    public bool WasCleared { get; set; }
}
