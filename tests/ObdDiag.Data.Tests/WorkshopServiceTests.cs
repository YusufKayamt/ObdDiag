using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ObdDiag.Data;
using ObdDiag.Data.Entities;

namespace ObdDiag.Data.Tests;

public class WorkshopServiceTests : IDisposable
{
    private const string Vin1 = "WVWZZZ1JZXW000001";
    private const string Vin2 = "WDB2030461A123456";

    private readonly TestDatabase _db = new();
    private IWorkshopService Service => _db.Service;

    public void Dispose() => _db.Dispose();

    private Task<Customer> AddCustomerAsync(string name = "Ali Yılmaz", string? phone = "0532 111 22 33") =>
        Service.SaveCustomerAsync(new Customer { FullName = name, Phone = phone });

    private async Task<Vehicle> AddVehicleAsync(int customerId, string vin = Vin1, string? plate = "34 abc 123") =>
        await Service.SaveVehicleAsync(new Vehicle { CustomerId = customerId, Vin = vin, Plate = plate, Make = "Volkswagen", Model = "Golf", Year = 2001 });

    // ================= Müşteri =================

    [Fact]
    public async Task Customer_Can_Be_Added_And_Listed()
    {
        var saved = await Service.SaveCustomerAsync(new Customer
        {
            FullName = "  Ali Yılmaz ", Phone = " 0532 ", Email = "ali@example.com", Address = "Elazığ", Notes = "  "
        });

        saved.Id.Should().BeGreaterThan(0);
        saved.FullName.Should().Be("Ali Yılmaz");   // boşluklar temizlenir
        saved.Notes.Should().BeNull();              // boş metin null olur
        saved.CreatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));

        var list = await Service.GetCustomersAsync();
        list.Should().ContainSingle().Which.Email.Should().Be("ali@example.com");
    }

    [Fact]
    public async Task Customer_Can_Be_Updated()
    {
        var c = await AddCustomerAsync();

        await Service.SaveCustomerAsync(new Customer { Id = c.Id, FullName = "Ali Veli", Phone = "111" });

        var list = await Service.GetCustomersAsync();
        list.Should().ContainSingle();
        list[0].FullName.Should().Be("Ali Veli");
        list[0].Phone.Should().Be("111");
        list[0].CreatedAt.Should().Be(c.CreatedAt); // oluşturma tarihi değişmez
    }

    [Theory]
    [InlineData("", "boş")]
    [InlineData("   ", "boş")]
    public async Task Customer_Name_Is_Required(string name, string messagePart)
    {
        var act = () => Service.SaveCustomerAsync(new Customer { FullName = name });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage($"*{messagePart}*");
    }

    [Fact]
    public async Task Customer_Email_Must_Contain_At_Sign()
    {
        var act = () => Service.SaveCustomerAsync(new Customer { FullName = "Ali", Email = "aliexample.com" });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*E-posta*");
    }

    [Fact]
    public async Task Customer_Too_Long_Name_Is_Rejected()
    {
        var act = () => Service.SaveCustomerAsync(new Customer { FullName = new string('a', 201) });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*200*");
    }

    [Fact]
    public async Task Updating_Missing_Customer_Gives_Friendly_Error()
    {
        var act = () => Service.SaveCustomerAsync(new Customer { Id = 999, FullName = "X" });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*bulunamadı*");
    }

    [Fact]
    public async Task Customers_Are_Sorted_By_Name()
    {
        await AddCustomerAsync("Zeynep");
        await AddCustomerAsync("Ahmet");
        await AddCustomerAsync("Mehmet");

        (await Service.GetCustomersAsync()).Select(c => c.FullName).Should().Equal("Ahmet", "Mehmet", "Zeynep");
    }

    [Fact]
    public async Task Search_Finds_By_Name_Phone_Plate_And_Vin()
    {
        var ali = await AddCustomerAsync("Ali Yılmaz", "0532 111");
        var veli = await AddCustomerAsync("Veli Kaya", "0555 999");
        await AddVehicleAsync(ali.Id, Vin1, "34 ABC 123");
        await AddVehicleAsync(veli.Id, Vin2, "23 XYZ 789");

        (await Service.GetCustomersAsync("yılmaz")).Select(c => c.FullName).Should().Equal("Ali Yılmaz");
        (await Service.GetCustomersAsync("0555")).Select(c => c.FullName).Should().Equal("Veli Kaya");
        (await Service.GetCustomersAsync("xyz")).Select(c => c.FullName).Should().Equal("Veli Kaya");
        (await Service.GetCustomersAsync("WVWZZZ")).Select(c => c.FullName).Should().Equal("Ali Yılmaz");
        (await Service.GetCustomersAsync("yok böyle biri")).Should().BeEmpty();
        (await Service.GetCustomersAsync("  ")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_Treats_Percent_And_Underscore_As_Normal_Characters()
    {
        await AddCustomerAsync("Ali Yılmaz");
        (await Service.GetCustomersAsync("%")).Should().BeEmpty();
        (await Service.GetCustomersAsync("_")).Should().BeEmpty();
    }

    [Fact]
    public async Task Customer_Without_Vehicles_Can_Be_Deleted()
    {
        var c = await AddCustomerAsync();

        await Service.DeleteCustomerAsync(c.Id);

        (await Service.GetCustomersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Customer_With_Vehicle_Cannot_Be_Deleted()
    {
        var c = await AddCustomerAsync();
        await AddVehicleAsync(c.Id);

        var act = () => Service.DeleteCustomerAsync(c.Id);

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*araçları var*");
        (await Service.GetCustomersAsync()).Should().HaveCount(1);
    }

    // ================= Araç =================

    [Fact]
    public async Task Vehicle_Can_Be_Added_With_Normalized_Fields()
    {
        var c = await AddCustomerAsync();

        var v = await Service.SaveVehicleAsync(new Vehicle
        {
            CustomerId = c.Id, Vin = " wvwzzz1jzxw000001 ", Plate = " 34 abc 123 ", Make = "VW", Year = 2001
        });

        v.Id.Should().BeGreaterThan(0);
        v.Vin.Should().Be(Vin1);
        v.Plate.Should().Be("34 ABC 123");
        (await Service.GetVehiclesAsync(c.Id)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "boş")]
    [InlineData("ABC", "17 karakter")]
    [InlineData("IVWZZZ1JZXW000001", "I, O ve Q")]
    [InlineData("WVWZZZ1JZXW00000O", "I, O ve Q")]
    [InlineData("WVWZZZ1JZXW00000Q", "I, O ve Q")]
    [InlineData("WVWZZZ1JZXW00000-", "harf ve rakam")]
    public async Task Invalid_Vin_Is_Rejected(string vin, string messagePart)
    {
        var c = await AddCustomerAsync();

        var act = () => Service.SaveVehicleAsync(new Vehicle { CustomerId = c.Id, Vin = vin });

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage($"*{messagePart}*");
        (await Service.GetVehiclesAsync(c.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_Vin_Is_Rejected_Even_For_Another_Customer_And_Different_Case()
    {
        var ali = await AddCustomerAsync("Ali");
        var veli = await AddCustomerAsync("Veli");
        await AddVehicleAsync(ali.Id, Vin1, "34 ABC 123");

        var act = () => Service.SaveVehicleAsync(new Vehicle { CustomerId = veli.Id, Vin = Vin1.ToLowerInvariant() });

        var ex = await act.Should().ThrowAsync<DataValidationException>();
        ex.WithMessage("*zaten kayıtlı*").And.Message.Should().Contain("34 ABC 123").And.Contain("Ali");
        (await Service.GetVehiclesAsync(veli.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Database_Itself_Enforces_Unique_Vin()
    {
        // Servisin kontrolünü atlayıp doğrudan veritabanına yazmayı deniyoruz: veritabanı da reddetmeli.
        var c = await AddCustomerAsync();
        await AddVehicleAsync(c.Id, Vin1);

        await using var raw = _db.CreateDbContext();
        raw.Vehicles.Add(new Vehicle { CustomerId = c.Id, Vin = Vin1 });

        var act = () => raw.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Vehicle_Can_Be_Updated_Keeping_Its_Own_Vin()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        await Service.SaveVehicleAsync(new Vehicle { Id = v.Id, CustomerId = c.Id, Vin = Vin1, Plate = "06 NEW 06", Year = 2002 });

        var list = await Service.GetVehiclesAsync(c.Id);
        list.Should().ContainSingle();
        list[0].Plate.Should().Be("06 NEW 06");
        list[0].Year.Should().Be(2002);
    }

    [Fact]
    public async Task Vehicle_Cannot_Be_Updated_To_Another_Vehicles_Vin()
    {
        var c = await AddCustomerAsync();
        await AddVehicleAsync(c.Id, Vin1, "A");
        var second = await AddVehicleAsync(c.Id, Vin2, "B");

        var act = () => Service.SaveVehicleAsync(new Vehicle { Id = second.Id, CustomerId = c.Id, Vin = Vin1 });

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*zaten kayıtlı*");
    }

    [Fact]
    public async Task Vehicle_Can_Move_To_Another_Customer()
    {
        var ali = await AddCustomerAsync("Ali");
        var veli = await AddCustomerAsync("Veli");
        var v = await AddVehicleAsync(ali.Id);

        await Service.SaveVehicleAsync(new Vehicle { Id = v.Id, CustomerId = veli.Id, Vin = Vin1 });

        (await Service.GetVehiclesAsync(ali.Id)).Should().BeEmpty();
        (await Service.GetVehiclesAsync(veli.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Vehicle_Needs_An_Existing_Customer()
    {
        var act = () => Service.SaveVehicleAsync(new Vehicle { CustomerId = 123, Vin = Vin1 });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*müşteriye bağlı*");
    }

    [Theory]
    [InlineData(1949)]
    [InlineData(2500)]
    public async Task Vehicle_Year_Must_Be_Reasonable(int year)
    {
        var c = await AddCustomerAsync();
        var act = () => Service.SaveVehicleAsync(new Vehicle { CustomerId = c.Id, Vin = Vin1, Year = year });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*yılı*");
    }

    [Fact]
    public async Task FindVehicleByVin_Returns_Vehicle_With_Customer_Ignoring_Case_And_Spaces()
    {
        var c = await AddCustomerAsync("Ali Yılmaz");
        await AddVehicleAsync(c.Id);

        var found = await Service.FindVehicleByVinAsync(" wvwzzz1jzxw000001 ");

        found.Should().NotBeNull();
        found!.Plate.Should().Be("34 ABC 123");
        found.Customer!.FullName.Should().Be("Ali Yılmaz");
    }

    [Fact]
    public async Task FindVehicleByVin_Returns_Null_When_Unknown()
    {
        (await Service.FindVehicleByVinAsync(Vin2)).Should().BeNull();
        (await Service.FindVehicleByVinAsync("")).Should().BeNull();
    }

    [Fact]
    public async Task Vehicle_Without_History_Can_Be_Deleted()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        await Service.DeleteVehicleAsync(v.Id);

        (await Service.GetVehiclesAsync(c.Id)).Should().BeEmpty();
        // Silinen VIN tekrar kullanılabilir
        await AddVehicleAsync(c.Id, Vin1);
    }

    [Fact]
    public async Task Vehicle_With_Repair_History_Cannot_Be_Deleted()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);
        await Service.AddRepairAsync(new RepairRecord { VehicleId = v.Id, Description = "Yağ değişimi" });

        var act = () => Service.DeleteVehicleAsync(v.Id);

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*tamir geçmişi*");
        (await Service.GetVehiclesAsync(c.Id)).Should().HaveCount(1);
    }

    // ================= Tamir geçmişi =================

    [Fact]
    public async Task Repair_Is_Saved_With_Dtcs_And_Snapshot()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        var saved = await Service.AddRepairAsync(new RepairRecord
        {
            VehicleId = v.Id,
            Date = new DateTime(2025, 3, 10, 14, 30, 0),
            Mileage = 182_500,
            Description = "  Gaz kelebeği temizlendi, arıza kodları silindi.  ",
            DtcRecords =
            {
                new DtcRecord { Code = "p0104", Description = "Hava akış sensörü", WasCleared = true },
                new DtcRecord { Code = "P0301" },
                new DtcRecord { Code = "  " } // boş kod atlanır
            },
            LiveDataSnapshots =
            {
                new LiveDataSnapshot { Rpm = 1726, Speed = 0, CoolantTemp = 83, Throttle = null }
            }
        });

        saved.Id.Should().BeGreaterThan(0);

        var history = await Service.GetRepairHistoryAsync(v.Id);
        var r = history.Should().ContainSingle().Subject;
        r.Description.Should().Be("Gaz kelebeği temizlendi, arıza kodları silindi.");
        r.Date.Should().Be(new DateTime(2025, 3, 10, 14, 30, 0));
        r.Mileage.Should().Be(182_500);
        r.DtcRecords.Select(d => d.Code).Should().BeEquivalentTo("P0104", "P0301");
        r.DtcRecords.Single(d => d.Code == "P0104").WasCleared.Should().BeTrue();
        r.DtcRecords.Single(d => d.Code == "P0104").Description.Should().Be("Hava akış sensörü");
        r.LiveDataSnapshots.Should().ContainSingle();
        var snap = r.LiveDataSnapshots[0];
        snap.Rpm.Should().Be(1726);
        snap.CoolantTemp.Should().Be(83);
        snap.Throttle.Should().BeNull();
        snap.CapturedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Repair_History_Is_Newest_First_And_Per_Vehicle()
    {
        var c = await AddCustomerAsync();
        var v1 = await AddVehicleAsync(c.Id, Vin1);
        var v2 = await AddVehicleAsync(c.Id, Vin2);
        await Service.AddRepairAsync(new RepairRecord { VehicleId = v1.Id, Date = new DateTime(2024, 1, 1), Description = "eski" });
        await Service.AddRepairAsync(new RepairRecord { VehicleId = v1.Id, Date = new DateTime(2025, 6, 1), Description = "yeni" });
        await Service.AddRepairAsync(new RepairRecord { VehicleId = v1.Id, Date = new DateTime(2024, 9, 1), Description = "orta" });
        await Service.AddRepairAsync(new RepairRecord { VehicleId = v2.Id, Date = new DateTime(2025, 1, 1), Description = "başka araç" });

        (await Service.GetRepairHistoryAsync(v1.Id)).Select(r => r.Description).Should().Equal("yeni", "orta", "eski");
        (await Service.GetRepairHistoryAsync(v2.Id)).Select(r => r.Description).Should().Equal("başka araç");
    }

    [Fact]
    public async Task Repair_Without_Date_Gets_Current_Time()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        var saved = await Service.AddRepairAsync(new RepairRecord { VehicleId = v.Id, Description = "x" });

        saved.Date.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Repair_Requires_Description()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        var act = () => Service.AddRepairAsync(new RepairRecord { VehicleId = v.Id, Description = "  " });

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*açıklaması boş*");
    }

    [Fact]
    public async Task Repair_Requires_Existing_Vehicle()
    {
        var act = () => Service.AddRepairAsync(new RepairRecord { VehicleId = 42, Description = "x" });
        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*araca bağlı*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5_000_001)]
    public async Task Repair_Mileage_Must_Be_Reasonable(int km)
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);

        var act = () => Service.AddRepairAsync(new RepairRecord { VehicleId = v.Id, Description = "x", Mileage = km });

        (await act.Should().ThrowAsync<DataValidationException>()).WithMessage("*Kilometre*");
    }

    [Fact]
    public async Task Deleting_A_Repair_Record_Also_Deletes_Its_Dtcs_And_Snapshots()
    {
        var c = await AddCustomerAsync();
        var v = await AddVehicleAsync(c.Id);
        var repair = await Service.AddRepairAsync(new RepairRecord
        {
            VehicleId = v.Id, Description = "x",
            DtcRecords = { new DtcRecord { Code = "P0104" } },
            LiveDataSnapshots = { new LiveDataSnapshot { Rpm = 1 } }
        });

        await using (var raw = _db.CreateDbContext())
        {
            raw.RepairRecords.Remove(await raw.RepairRecords.SingleAsync(r => r.Id == repair.Id));
            await raw.SaveChangesAsync();
        }

        await using var check = _db.CreateDbContext();
        (await check.DtcRecords.CountAsync()).Should().Be(0);
        (await check.LiveDataSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Data_Survives_In_A_Real_File_Database()
    {
        // Gerçek dosyaya yazıp yeni bir bağlantıyla tekrar okuyoruz: veriler kalıcı mı?
        var path = Path.Combine(Path.GetTempPath(), $"obddiag-test-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ObdDbContext>().UseSqlite($"Data Source={path}").Options;
            var factory = new SimpleFactory(options);
            await using (var db = factory.CreateDbContext())
                await db.Database.EnsureCreatedAsync();

            var first = new WorkshopService(factory);
            var c = await first.SaveCustomerAsync(new Customer { FullName = "Kalıcı Müşteri" });
            await first.SaveVehicleAsync(new Vehicle { CustomerId = c.Id, Vin = Vin1 });

            var second = new WorkshopService(new SimpleFactory(options));
            (await second.FindVehicleByVinAsync(Vin1))!.Customer!.FullName.Should().Be("Kalıcı Müşteri");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private sealed class SimpleFactory : IDbContextFactory<ObdDbContext>
    {
        private readonly DbContextOptions<ObdDbContext> _options;
        public SimpleFactory(DbContextOptions<ObdDbContext> options) => _options = options;
        public ObdDbContext CreateDbContext() => new(_options);
    }
}
