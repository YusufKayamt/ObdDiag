using Microsoft.EntityFrameworkCore;
using ObdDiag.Core.Parsing;
using ObdDiag.Data.Entities;

namespace ObdDiag.Data;

/// <summary>
/// <see cref="IWorkshopService"/> uygulaması. Her işlem için kısa ömürlü bir veritabanı bağlantısı (DbContext) açar;
/// WPF gibi uzun yaşayan uygulamalarda önerilen yöntem budur.
/// </summary>
public sealed class WorkshopService : IWorkshopService
{
    private const int MaxMileage = 5_000_000;

    private readonly IDbContextFactory<ObdDbContext> _factory;

    public WorkshopService(IDbContextFactory<ObdDbContext> factory)
    {
        _factory = factory;
    }

    // ---- Müşteriler ----

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var query = db.Customers.AsNoTracking();

        var text = search?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            // % ve _ karakterleri LIKE'ta özel anlam taşır; düz metin gibi aransın diye kaçırılır.
            var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(c =>
                EF.Functions.Like(c.FullName, pattern, "\\")
                || (c.Phone != null && EF.Functions.Like(c.Phone, pattern, "\\"))
                || c.Vehicles.Any(v => EF.Functions.Like(v.Vin, pattern, "\\")
                                       || (v.Plate != null && EF.Functions.Like(v.Plate, pattern, "\\"))));
        }

        return await query.OrderBy(c => c.FullName).ThenBy(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<Customer> SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var name = Clean(customer.FullName);
        if (name is null)
            throw new DataValidationException("Müşterinin adı soyadı boş olamaz.");
        RequireMax(name, 200, "Ad soyad");

        var phone = Clean(customer.Phone);
        RequireMax(phone, 50, "Telefon");
        var email = Clean(customer.Email);
        RequireMax(email, 200, "E-posta");
        if (email is not null && !email.Contains('@'))
            throw new DataValidationException("E-posta adresi geçerli görünmüyor (@ işareti yok).");
        var address = Clean(customer.Address);
        RequireMax(address, 500, "Adres");
        var notes = Clean(customer.Notes);
        RequireMax(notes, 1000, "Not");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        Customer entity;
        if (customer.Id == 0)
        {
            entity = new Customer { CreatedAt = DateTime.Now };
            db.Customers.Add(entity);
        }
        else
        {
            entity = await db.Customers.FindAsync(new object[] { customer.Id }, cancellationToken)
                     ?? throw new DataValidationException("Müşteri kaydı bulunamadı. Silinmiş olabilir.");
        }

        entity.FullName = name;
        entity.Phone = phone;
        entity.Email = email;
        entity.Address = address;
        entity.Notes = notes;

        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task DeleteCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Customers.FindAsync(new object[] { customerId }, cancellationToken)
                     ?? throw new DataValidationException("Müşteri kaydı bulunamadı. Silinmiş olabilir.");

        if (await db.Vehicles.AnyAsync(v => v.CustomerId == customerId, cancellationToken))
            throw new DataValidationException("Bu müşterinin kayıtlı araçları var. Müşteriyi silmek için önce araçlarını silin.");

        db.Customers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Araçlar ----

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Vehicles.AsNoTracking()
            .Where(v => v.CustomerId == customerId)
            .OrderBy(v => v.Plate).ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Vehicle?> FindVehicleByVinAsync(string vin, CancellationToken cancellationToken = default)
    {
        var normalized = VinValidator.Normalize(vin);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Vehicles.AsNoTracking()
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Vin == normalized, cancellationToken);
    }

    public async Task<Vehicle> SaveVehicleAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        var vinError = VinValidator.GetError(vehicle.Vin);
        if (vinError is not null)
            throw new DataValidationException(vinError);
        var vin = VinValidator.Normalize(vehicle.Vin);

        var plate = Clean(vehicle.Plate)?.ToUpperInvariant();
        RequireMax(plate, 20, "Plaka");
        var make = Clean(vehicle.Make);
        RequireMax(make, 100, "Marka");
        var model = Clean(vehicle.Model);
        RequireMax(model, 100, "Model");
        var notes = Clean(vehicle.Notes);
        RequireMax(notes, 1000, "Not");

        if (vehicle.Year is { } year && (year < 1950 || year > DateTime.Now.Year + 1))
            throw new DataValidationException($"Model yılı 1950 ile {DateTime.Now.Year + 1} arasında olmalı.");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        if (!await db.Customers.AnyAsync(c => c.Id == vehicle.CustomerId, cancellationToken))
            throw new DataValidationException("Araç bir müşteriye bağlı olmalı. Önce müşteriyi seçin.");

        var duplicate = await db.Vehicles
            .Where(v => v.Vin == vin && v.Id != vehicle.Id)
            .Select(v => new { v.Plate, Owner = v.Customer!.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
        {
            var plateText = duplicate.Plate is null ? "" : $"{duplicate.Plate}, ";
            throw new DataValidationException($"Bu şasi numarası (VIN) zaten kayıtlı ({plateText}müşteri: {duplicate.Owner}).");
        }

        Vehicle entity;
        if (vehicle.Id == 0)
        {
            entity = new Vehicle { CreatedAt = DateTime.Now };
            db.Vehicles.Add(entity);
        }
        else
        {
            entity = await db.Vehicles.FindAsync(new object[] { vehicle.Id }, cancellationToken)
                     ?? throw new DataValidationException("Araç kaydı bulunamadı. Silinmiş olabilir.");
        }

        entity.CustomerId = vehicle.CustomerId;
        entity.Vin = vin;
        entity.Plate = plate;
        entity.Make = make;
        entity.Model = model;
        entity.Year = vehicle.Year;
        entity.Notes = notes;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // İki kişi aynı anda aynı VIN'i eklemeye çalışırsa benzersizlik kuralı veritabanı düzeyinde yakalar.
            throw new DataValidationException("Araç kaydedilemedi. Bu şasi numarası (VIN) başka bir kayıtta olabilir.");
        }
        return entity;
    }

    public async Task DeleteVehicleAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Vehicles.FindAsync(new object[] { vehicleId }, cancellationToken)
                     ?? throw new DataValidationException("Araç kaydı bulunamadı. Silinmiş olabilir.");

        if (await db.RepairRecords.AnyAsync(r => r.VehicleId == vehicleId, cancellationToken))
            throw new DataValidationException("Bu aracın tamir geçmişi var, silinemez.");

        db.Vehicles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Tamir geçmişi ----

    public async Task<IReadOnlyList<RepairRecord>> GetRepairHistoryAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.RepairRecords.AsNoTracking()
            .Include(r => r.DtcRecords)
            .Include(r => r.LiveDataSnapshots)
            .Where(r => r.VehicleId == vehicleId)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<RepairRecord> AddRepairAsync(RepairRecord repair, CancellationToken cancellationToken = default)
    {
        var description = Clean(repair.Description);
        if (description is null)
            throw new DataValidationException("Yapılan işlemin açıklaması boş olamaz.");
        RequireMax(description, 2000, "Açıklama");

        if (repair.Mileage is { } km && (km < 0 || km > MaxMileage))
            throw new DataValidationException("Kilometre değeri geçerli görünmüyor.");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Vehicles.AnyAsync(v => v.Id == repair.VehicleId, cancellationToken))
            throw new DataValidationException("Tamir kaydı bir araca bağlı olmalı.");

        // Gelen nesneyi olduğu gibi eklemek yerine alanları yeni nesnelere kopyalıyoruz.
        var entity = new RepairRecord
        {
            VehicleId = repair.VehicleId,
            Date = repair.Date == default ? DateTime.Now : repair.Date,
            Mileage = repair.Mileage,
            Description = description,
            DtcRecords = repair.DtcRecords
                .Where(d => !string.IsNullOrWhiteSpace(d.Code))
                .Select(d => new DtcRecord
                {
                    Code = d.Code.Trim().ToUpperInvariant(),
                    Description = Clean(d.Description),
                    WasCleared = d.WasCleared
                }).ToList(),
            LiveDataSnapshots = repair.LiveDataSnapshots
                .Select(s => new LiveDataSnapshot
                {
                    CapturedAt = s.CapturedAt == default ? DateTime.Now : s.CapturedAt,
                    Rpm = s.Rpm,
                    Speed = s.Speed,
                    CoolantTemp = s.CoolantTemp,
                    Throttle = s.Throttle
                }).ToList()
        };

        db.RepairRecords.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    // ---- Yardımcılar ----

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static void RequireMax(string? text, int max, string label)
    {
        if (text is not null && text.Length > max)
            throw new DataValidationException($"{label} en fazla {max} karakter olabilir.");
    }
}
