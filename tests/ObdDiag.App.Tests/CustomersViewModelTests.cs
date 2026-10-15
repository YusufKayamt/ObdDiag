using FluentAssertions;
using Moq;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Data.Entities;

namespace ObdDiag.App.Tests;

public class CustomersViewModelTests : IDisposable
{
    private const string Vin1 = "WVWZZZ1JZXW000001";
    private const string Vin2 = "WDB2030461A123456";

    private readonly InMemoryDatabase _db = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly CustomersViewModel _vm;

    public CustomersViewModelTests()
    {
        _vm = new CustomersViewModel(_db.Service, _dialogs.Object);
    }

    public void Dispose() => _db.Dispose();

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Beklenen durum oluşmadı.");
            await Task.Delay(10);
        }
    }

    /// <summary>Formu doldurup yeni müşteri kaydeder ve seçili hale getirir.</summary>
    private async Task AddCustomerViaUiAsync(string name, string? phone = null)
    {
        _vm.NewCustomerCommand.Execute(null);
        _vm.CustomerName = name;
        _vm.CustomerPhone = phone ?? "";
        await _vm.SaveCustomerCommand.ExecuteAsync(null);
        await WaitUntil(() => _vm.SelectedCustomer?.FullName == name);
    }

    private async Task AddVehicleViaUiAsync(string vin, string plate)
    {
        _vm.NewVehicleCommand.Execute(null);
        _vm.VehicleVin = vin;
        _vm.VehiclePlate = plate;
        _vm.VehicleMake = "Volkswagen";
        _vm.VehicleModel = "Golf";
        _vm.VehicleYear = "2001";
        await _vm.SaveVehicleCommand.ExecuteAsync(null);
        // Kayıtta VIN büyük harfe çevrildiği için karşılaştırma harf büyüklüğünü yok sayar.
        await WaitUntil(() => string.Equals(_vm.SelectedVehicle?.Vin, vin, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Müşteri ----

    [Fact]
    public async Task Starts_Empty()
    {
        await _vm.InitializeAsync();

        _vm.Customers.Should().BeEmpty();
        _vm.SelectedCustomer.Should().BeNull();
        _vm.DeleteCustomerCommand.CanExecute(null).Should().BeFalse();
        _vm.SaveVehicleCommand.CanExecute(null).Should().BeFalse();
        _vm.AddRepairCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Customer_Is_Saved_Listed_And_Selected()
    {
        await _vm.InitializeAsync();

        await AddCustomerViaUiAsync("Ali Yılmaz", "0532 111");

        _vm.Customers.Should().ContainSingle();
        _vm.SelectedCustomer!.Phone.Should().Be("0532 111");
        _vm.Message.Should().Contain("kaydedildi");
        _vm.IsError.Should().BeFalse();
        _vm.DeleteCustomerCommand.CanExecute(null).Should().BeTrue();
        _vm.SaveVehicleCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Customer_Without_Name_Shows_Friendly_Error()
    {
        await _vm.InitializeAsync();
        _vm.NewCustomerCommand.Execute(null);
        _vm.CustomerName = "  ";

        await _vm.SaveCustomerCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("boş olamaz");
        _vm.Customers.Should().BeEmpty();
    }

    [Fact]
    public async Task Selecting_A_Customer_Fills_The_Form()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz", "0532 111");
        await AddCustomerViaUiAsync("Veli Kaya", "0555 222");

        _vm.SelectedCustomer = _vm.Customers.Single(c => c.FullName == "Ali Yılmaz");

        _vm.CustomerName.Should().Be("Ali Yılmaz");
        _vm.CustomerPhone.Should().Be("0532 111");
    }

    [Fact]
    public async Task Existing_Customer_Is_Updated_Not_Duplicated()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");

        _vm.CustomerName = "Ali Veli Yılmaz";
        await _vm.SaveCustomerCommand.ExecuteAsync(null);
        await WaitUntil(() => _vm.SelectedCustomer?.FullName == "Ali Veli Yılmaz");

        _vm.Customers.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_Filters_The_List()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");
        await AddCustomerViaUiAsync("Veli Kaya");

        _vm.SearchText = "kaya";
        await WaitUntil(() => _vm.Customers.Count == 1);

        _vm.Customers.Single().FullName.Should().Be("Veli Kaya");

        _vm.SearchText = "";
        await WaitUntil(() => _vm.Customers.Count == 2);
    }

    [Fact]
    public async Task Delete_Customer_Asks_And_Removes_When_Approved()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");

        await _vm.DeleteCustomerCommand.ExecuteAsync(null);

        _dialogs.Verify(d => d.Confirm(It.IsAny<string>(), It.Is<string>(m => m.Contains("Ali Yılmaz"))), Times.Once);
        _vm.Customers.Should().BeEmpty();
        _vm.SelectedCustomer.Should().BeNull();
        _vm.CustomerName.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_Customer_Does_Nothing_When_Declined()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");

        await _vm.DeleteCustomerCommand.ExecuteAsync(null);

        _vm.Customers.Should().ContainSingle();
    }

    [Fact]
    public async Task Customer_With_Vehicle_Cannot_Be_Deleted_And_Explains_Why()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");
        await AddVehicleViaUiAsync(Vin1, "34 abc 123");

        await _vm.DeleteCustomerCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("araçları var");
        _vm.Customers.Should().ContainSingle();
    }

    // ---- Araç ----

    [Fact]
    public async Task Vehicle_Is_Saved_With_Normalized_Values_And_Selected()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali Yılmaz");

        await AddVehicleViaUiAsync(Vin1.ToLowerInvariant(), "34 abc 123");

        _vm.Vehicles.Should().ContainSingle();
        _vm.SelectedVehicle!.Vin.Should().Be(Vin1);
        _vm.SelectedVehicle.Plate.Should().Be("34 ABC 123");
        _vm.SelectedVehicle.Year.Should().Be(2001);
        _vm.VehicleVin.Should().Be(Vin1);
        _vm.Message.Should().Contain("Araç kaydedildi");
    }

    [Fact]
    public async Task Vehicle_List_Follows_The_Selected_Customer()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "A");
        await AddCustomerViaUiAsync("Veli");
        await AddVehicleViaUiAsync(Vin2, "B");

        _vm.SelectedCustomer = _vm.Customers.Single(c => c.FullName == "Ali");
        await WaitUntil(() => _vm.Vehicles.Count == 1 && _vm.Vehicles[0].Vin == Vin1);

        _vm.SelectedCustomer = _vm.Customers.Single(c => c.FullName == "Veli");
        await WaitUntil(() => _vm.Vehicles.Count == 1 && _vm.Vehicles[0].Vin == Vin2);
    }

    [Theory]
    [InlineData("KISA", "17 karakter")]
    [InlineData("IVWZZZ1JZXW000001", "I, O ve Q")]
    public async Task Invalid_Vin_Shows_Friendly_Error(string vin, string messagePart)
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        _vm.NewVehicleCommand.Execute(null);
        _vm.VehicleVin = vin;

        await _vm.SaveVehicleCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain(messagePart);
        _vm.Vehicles.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_Vin_Shows_Friendly_Error()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "34 ABC 123");
        await AddCustomerViaUiAsync("Veli");

        _vm.NewVehicleCommand.Execute(null);
        _vm.VehicleVin = Vin1;
        await _vm.SaveVehicleCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("zaten kayıtlı").And.Contain("Ali");
    }

    [Fact]
    public async Task Non_Numeric_Year_Is_Rejected_Before_Saving()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        _vm.NewVehicleCommand.Execute(null);
        _vm.VehicleVin = Vin1;
        _vm.VehicleYear = "iki bin";

        await _vm.SaveVehicleCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("rakam");
        _vm.Vehicles.Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_A_Vehicle_Raises_VehicleSaved_Event()
    {
        Vehicle? raised = null;
        _vm.VehicleSaved += (_, v) => raised = v;
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");

        await AddVehicleViaUiAsync(Vin1, "A");

        raised.Should().NotBeNull();
        raised!.Vin.Should().Be(Vin1);
    }

    [Fact]
    public async Task Vehicle_Without_History_Can_Be_Deleted()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "34 ABC 123");

        await _vm.DeleteVehicleCommand.ExecuteAsync(null);

        _vm.Vehicles.Should().BeEmpty();
        _vm.SelectedVehicle.Should().BeNull();
        _vm.VehicleVin.Should().BeEmpty();
    }

    [Fact]
    public async Task Vehicle_With_History_Cannot_Be_Deleted()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "34 ABC 123");
        _vm.NewRepairDescription = "Yağ değişimi";
        await _vm.AddRepairCommand.ExecuteAsync(null);

        await _vm.DeleteVehicleCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("tamir geçmişi");
        _vm.Vehicles.Should().ContainSingle();
    }

    [Fact]
    public async Task PrepareNewVehicle_Fills_Vin_And_Guides_The_User()
    {
        await _vm.InitializeAsync();

        _vm.PrepareNewVehicle(Vin1);
        _vm.VehicleVin.Should().Be(Vin1);
        _vm.Message.Should().Contain("müşteri seçin");

        await AddCustomerViaUiAsync("Ali");
        _vm.PrepareNewVehicle(Vin2);
        _vm.VehicleVin.Should().Be(Vin2);
        _vm.Message.Should().Contain("Ali");
        _vm.SelectedVehicle.Should().BeNull();
    }

    [Fact]
    public async Task Pending_Vin_Survives_Selecting_A_Customer_And_Is_Saved_With_It()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddCustomerViaUiAsync("Veli");

        // Canlı ekrandan "Yeni Araç Olarak Kaydet" ile gelindi, sonra müşteri seçiliyor
        _vm.PrepareNewVehicle(Vin1);
        _vm.SelectedCustomer = _vm.Customers.Single(c => c.FullName == "Ali");
        await WaitUntil(() => _vm.SelectedCustomer?.FullName == "Ali");
        await Task.Delay(100); // araç listesinin yüklenmesi beklensin

        _vm.VehicleVin.Should().Be(Vin1);

        _vm.VehiclePlate = "34 ABC 123";
        await _vm.SaveVehicleCommand.ExecuteAsync(null);
        await WaitUntil(() => _vm.SelectedVehicle?.Vin == Vin1);
        _vm.Vehicles.Should().ContainSingle();

        // Kayıttan sonra bekleyen VIN unutulur: başka müşteriye geçince form boş olur
        _vm.SelectedCustomer = _vm.Customers.Single(c => c.FullName == "Veli");
        await WaitUntil(() => _vm.SelectedVehicle is null && _vm.Vehicles.Count == 0);
        await Task.Delay(100);
        _vm.VehicleVin.Should().BeEmpty();
    }

    [Fact]
    public async Task New_Vehicle_Button_Forgets_The_Pending_Vin()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        _vm.PrepareNewVehicle(Vin1);

        _vm.NewVehicleCommand.Execute(null);
        _vm.SelectedCustomer = null;
        await WaitUntil(() => _vm.SelectedCustomer is null);
        _vm.SelectedCustomer = _vm.Customers.Single();
        await Task.Delay(100);

        _vm.VehicleVin.Should().BeEmpty();
    }

    // ---- Tamir geçmişi ----

    [Fact]
    public async Task Manual_Repair_Appears_In_History()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "34 ABC 123");

        _vm.NewRepairDescription = "Triger kayışı değişti";
        _vm.NewRepairMileage = "182.500";
        await _vm.AddRepairCommand.ExecuteAsync(null);

        _vm.History.Should().ContainSingle();
        var item = _vm.History[0];
        item.Description.Should().Be("Triger kayışı değişti");
        item.MileageText.Should().Be("182.500 km");
        item.CodesText.Should().BeNull();
        _vm.NewRepairDescription.Should().BeEmpty();
        _vm.NewRepairMileage.Should().BeEmpty();
    }

    [Fact]
    public async Task Repair_Without_Description_Shows_Error()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "A");

        await _vm.AddRepairCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.Message.Should().Contain("açıklaması boş");
        _vm.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Repair_With_Bad_Mileage_Is_Rejected()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "A");
        _vm.NewRepairDescription = "x";
        _vm.NewRepairMileage = "çok";

        await _vm.AddRepairCommand.ExecuteAsync(null);

        _vm.IsError.Should().BeTrue();
        _vm.History.Should().BeEmpty();
    }

    [Fact]
    public async Task History_Shows_Codes_Cleared_Flag_And_Live_Data_And_Switches_With_Vehicle()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "A");
        var firstVehicle = _vm.SelectedVehicle!;

        await _db.Service.AddRepairAsync(new RepairRecord
        {
            VehicleId = firstVehicle.Id,
            Date = new DateTime(2025, 3, 10, 14, 30, 0),
            Description = "Arıza giderildi",
            DtcRecords = { new DtcRecord { Code = "P0104", WasCleared = true }, new DtcRecord { Code = "P0301" } },
            LiveDataSnapshots = { new LiveDataSnapshot { Rpm = 1726, Speed = 0, CoolantTemp = 83, Throttle = 12.4 } }
        });
        await _vm.RefreshHistoryAsync(firstVehicle.Id);

        var item = _vm.History.Should().ContainSingle().Subject;
        item.DateText.Should().Be("10.03.2025 14:30");
        item.CodesText.Should().Contain("P0104 (silindi)").And.Contain("P0301");
        item.SnapshotText.Should().Contain("1726 devir/dk").And.Contain("83 °C").And.Contain("gaz %12");

        // Başka bir araç seçilince liste değişir
        await AddVehicleViaUiAsync(Vin2, "B");
        _vm.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_For_A_Different_Vehicle_Does_Not_Touch_The_List()
    {
        await _vm.InitializeAsync();
        await AddCustomerViaUiAsync("Ali");
        await AddVehicleViaUiAsync(Vin1, "A");

        await _vm.RefreshHistoryAsync(9999);

        _vm.History.Should().BeEmpty();
    }

    // ---- Hata dayanıklılığı ----

    [Fact]
    public async Task Database_Failure_Is_Shown_As_Message_Not_Crash()
    {
        var broken = new Mock<ObdDiag.Data.IWorkshopService>();
        broken.Setup(w => w.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk hatası"));
        var vm = new CustomersViewModel(broken.Object, _dialogs.Object);

        await vm.InitializeAsync();

        vm.IsError.Should().BeTrue();
        vm.Message.Should().Contain("Veritabanında bir sorun").And.Contain("disk hatası");
    }
}
