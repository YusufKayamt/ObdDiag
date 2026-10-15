namespace ObdDiag.Data.Entities;

/// <summary>Servisin müşterisi.</summary>
public class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Vehicle> Vehicles { get; set; } = new();
}
