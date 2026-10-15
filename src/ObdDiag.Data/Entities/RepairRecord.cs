namespace ObdDiag.Data.Entities;

/// <summary>Bir aracın tamir/teşhis kaydı (tamir geçmişinin bir satırı).</summary>
public class RepairRecord
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime Date { get; set; }

    /// <summary>Araç kilometresi (biliniyorsa).</summary>
    public int? Mileage { get; set; }

    /// <summary>Yapılan işlemin açıklaması.</summary>
    public string Description { get; set; } = string.Empty;

    public List<DtcRecord> DtcRecords { get; set; } = new();
    public List<LiveDataSnapshot> LiveDataSnapshots { get; set; } = new();
}
