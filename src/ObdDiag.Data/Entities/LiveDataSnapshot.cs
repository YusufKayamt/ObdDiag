namespace ObdDiag.Data.Entities;

/// <summary>Teşhis sırasında alınan anlık canlı veri görüntüsü. Okunamayan değer null'dır.</summary>
public class LiveDataSnapshot
{
    public int Id { get; set; }
    public int RepairRecordId { get; set; }
    public RepairRecord? RepairRecord { get; set; }

    public DateTime CapturedAt { get; set; }
    public double? Rpm { get; set; }
    public int? Speed { get; set; }
    public int? CoolantTemp { get; set; }
    public double? Throttle { get; set; }
}
