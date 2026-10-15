using ObdDiag.Data.Entities;

namespace ObdDiag.Data;

/// <summary>
/// Müşteri, araç ve tamir geçmişi kayıtlarını yöneten servis.
/// Kurallara uymayan işlemlerde <see cref="DataValidationException"/> fırlatır.
/// </summary>
public interface IWorkshopService
{
    // ---- Müşteriler ----

    /// <summary>Müşterileri ada göre sıralı getirir. Aramada ad, telefon, plaka veya VIN içinde geçen metin aranır.</summary>
    Task<IReadOnlyList<Customer>> GetCustomersAsync(string? search = null, CancellationToken cancellationToken = default);

    /// <summary>Id'si 0 ise yeni müşteri ekler, değilse mevcut müşteriyi günceller. Kaydedilmiş halini döner.</summary>
    Task<Customer> SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Müşteriyi siler. Kayıtlı aracı varsa silinemez.</summary>
    Task DeleteCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    // ---- Araçlar ----

    Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>VIN'e göre aracı (müşterisiyle birlikte) bulur; yoksa null.</summary>
    Task<Vehicle?> FindVehicleByVinAsync(string vin, CancellationToken cancellationToken = default);

    /// <summary>Id'si 0 ise yeni araç ekler, değilse günceller. VIN geçerli ve benzersiz olmalıdır.</summary>
    Task<Vehicle> SaveVehicleAsync(Vehicle vehicle, CancellationToken cancellationToken = default);

    /// <summary>Aracı siler. Tamir geçmişi varsa silinemez.</summary>
    Task DeleteVehicleAsync(int vehicleId, CancellationToken cancellationToken = default);

    // ---- Tamir geçmişi ----

    /// <summary>Aracın tamir kayıtlarını (arıza kodları ve canlı veri görüntüleriyle) yeniden eskiye getirir.</summary>
    Task<IReadOnlyList<RepairRecord>> GetRepairHistoryAsync(int vehicleId, CancellationToken cancellationToken = default);

    /// <summary>Yeni tamir kaydı ekler (arıza kodları ve canlı veri görüntüsü kayıtla birlikte eklenir).</summary>
    Task<RepairRecord> AddRepairAsync(RepairRecord repair, CancellationToken cancellationToken = default);
}
