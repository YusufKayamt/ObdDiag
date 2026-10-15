namespace ObdDiag.Data.Entities;

/// <summary>Bir müşteriye ait araç. Şasi numarası (VIN) benzersizdir.</summary>
public class Vehicle
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>17 karakterlik şasi numarası, büyük harfli. Aynı VIN iki kez kaydedilemez.</summary>
    public string Vin { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<RepairRecord> RepairRecords { get; set; } = new();
}
