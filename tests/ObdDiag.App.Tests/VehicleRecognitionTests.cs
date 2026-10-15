using FluentAssertions;
using Moq;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Models;
using ObdDiag.Data;
using ObdDiag.Data.Entities;
using ObdDiag.Hardware;

namespace ObdDiag.App.Tests;

/// <summary>Bağlanınca VIN okuma, kayıtlı aracı bulma ve teşhisi tamir geçmişine kaydetme testleri.</summary>
public class VehicleRecognitionTests
{
    private const string Vin = "WVWZZZ1JZXW000001";

    private readonly Mock<IComPortProvider> _ports = new();
    private readonly Mock<IObdServiceFactory> _factory = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly Mock<IObdService> _service = new();
    private readonly Mock<IWorkshopService> _workshop = new();

    private static readonly Vehicle KnownVehicle = new()
    {
        Id = 7, CustomerId = 3, Vin = Vin, Plate = "34 ABC 123", Make = "Volkswagen", Model = "Golf",
        Customer = new Customer { Id = 3, FullName = "Ali Yılmaz" }
    };

    public VehicleRecognitionTests()
    {
        _ports.Setup(p => p.GetPortNames()).Returns(Array.Empty<string>());
        _factory.Setup(f => f.Create(It.IsAny<ConnectionSettings>())).Returns(_service.Object);

        _service.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _service.Setup(s => s.DisconnectAsync()).Returns(Task.CompletedTask);
        _service.Setup(s => s.ReadVinAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Vin);
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new LiveData(1726, 60, 83, 50));
        _service.Setup(s => s.ReadFaultCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "P0104", "P0301" });
        _service.Setup(s => s.ClearFaultCodesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _workshop.Setup(w => w.FindVehicleByVinAsync(Vin, It.IsAny<CancellationToken>())).ReturnsAsync(KnownVehicle);
        _workshop.Setup(w => w.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Customer>());
        _workshop.Setup(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RepairRecord r, CancellationToken _) => r);
    }

    private MainViewModel Create() =>
        new(_ports.Object, _factory.Object, _dialogs.Object, _workshop.Object,
            new CustomersViewModel(_workshop.Object, _dialogs.Object))
        { PollInterval = TimeSpan.FromMilliseconds(10) };

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Beklenen durum oluşmadı.");
            await Task.Delay(10);
        }
    }

    private async Task<MainViewModel> ConnectedAsync()
    {
        var vm = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        return vm;
    }

    // ---- Araç tanıma ----

    [Fact]
    public async Task Connect_Reads_Vin_And_Finds_Registered_Vehicle()
    {
        var vm = await ConnectedAsync();

        vm.VinInput.Should().Be(Vin);
        vm.CurrentVehicle.Should().Be(KnownVehicle);
        vm.HasVehicle.Should().BeTrue();
        vm.CanRegisterVehicle.Should().BeFalse();
        vm.VehicleInfoText.Should().Contain("34 ABC 123").And.Contain("Volkswagen Golf").And.Contain("Ali Yılmaz");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Unknown_Vin_Offers_To_Register_The_Vehicle()
    {
        _workshop.Setup(w => w.FindVehicleByVinAsync(Vin, It.IsAny<CancellationToken>())).ReturnsAsync((Vehicle?)null);

        var vm = await ConnectedAsync();

        vm.CurrentVehicle.Should().BeNull();
        vm.CanRegisterVehicle.Should().BeTrue();
        vm.VehicleInfoText.Should().Contain("kayıtlı değil").And.Contain(Vin);
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Vin_Not_Readable_Still_Connects_And_Asks_For_Manual_Entry()
    {
        _service.Setup(s => s.ReadVinAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var vm = await ConnectedAsync();

        vm.IsConnected.Should().BeTrue();
        vm.CurrentVehicle.Should().BeNull();
        vm.CanRegisterVehicle.Should().BeFalse();
        vm.VehicleInfoText.Should().Contain("okunamadı").And.Contain("Aracı Bul");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Vin_Read_Error_Does_Not_Break_Connection()
    {
        _service.Setup(s => s.ReadVinAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ObdException("hata"));

        var vm = await ConnectedAsync();

        vm.IsConnected.Should().BeTrue();
        vm.IsStatusError.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Database_Error_During_Lookup_Does_Not_Break_Connection()
    {
        _workshop.Setup(w => w.FindVehicleByVinAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk dolu"));

        var vm = await ConnectedAsync();

        vm.IsConnected.Should().BeTrue();
        vm.CurrentVehicle.Should().BeNull();
        vm.VehicleInfoText.Should().Contain("sorun oluştu");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Elle VIN ----

    [Fact]
    public async Task Manual_Vin_Lookup_Works_Without_Connection()
    {
        var vm = Create();
        vm.VinInput = "wvwzzz1jzxw000001"; // küçük harf

        await vm.FindVehicleCommand.ExecuteAsync(null);

        vm.CurrentVehicle.Should().Be(KnownVehicle);
    }

    [Theory]
    [InlineData("ABC", "17 karakter")]
    [InlineData("IVWZZZ1JZXW000001", "I, O ve Q")]
    [InlineData("", "boş")]
    public async Task Manual_Invalid_Vin_Shows_Message_And_Does_Not_Query_Database(string vin, string messagePart)
    {
        var vm = Create();
        vm.VinInput = vin;

        await vm.FindVehicleCommand.ExecuteAsync(null);

        vm.CurrentVehicle.Should().BeNull();
        vm.VehicleInfoText.Should().Contain(messagePart);
        _workshop.Verify(w => w.FindVehicleByVinAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Register_Vehicle_Opens_Customers_Tab_With_Vin_Prefilled()
    {
        var vm = Create();
        vm.VinInput = " wvwzzz1jzxw000001 ";

        vm.RegisterVehicleCommand.Execute(null);

        vm.SelectedTabIndex.Should().Be(1);
        vm.Customers.VehicleVin.Should().Be(Vin);
        vm.Customers.Message.Should().Contain("müşteri");
    }

    [Fact]
    public void Register_Vehicle_With_Bad_Vin_Stays_On_Live_Tab()
    {
        var vm = Create();
        vm.VinInput = "KISA";

        vm.RegisterVehicleCommand.Execute(null);

        vm.SelectedTabIndex.Should().Be(0);
        vm.VehicleInfoText.Should().Contain("17 karakter");
    }

    [Fact]
    public async Task Saving_The_Vehicle_On_Customers_Tab_Refreshes_Live_Vehicle_Info()
    {
        _workshop.Setup(w => w.FindVehicleByVinAsync(Vin, It.IsAny<CancellationToken>())).ReturnsAsync((Vehicle?)null);
        var vm = await ConnectedAsync();
        vm.CanRegisterVehicle.Should().BeTrue();

        // Kullanıcı müşteri ekranında aracı kaydediyor
        _workshop.Setup(w => w.FindVehicleByVinAsync(Vin, It.IsAny<CancellationToken>())).ReturnsAsync(KnownVehicle);
        var customer = new Customer { Id = 3, FullName = "Ali Yılmaz" };
        _workshop.Setup(w => w.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { customer });
        _workshop.Setup(w => w.GetVehiclesAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { KnownVehicle });
        _workshop.Setup(w => w.GetRepairHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<RepairRecord>());
        _workshop.Setup(w => w.SaveVehicleAsync(It.IsAny<Vehicle>(), It.IsAny<CancellationToken>())).ReturnsAsync(KnownVehicle);
        await vm.Customers.InitializeAsync();
        vm.Customers.SelectedCustomer = vm.Customers.Customers.Single();
        await WaitUntil(() => vm.Customers.Vehicles.Count == 1);
        vm.Customers.VehicleVin = Vin;

        await vm.Customers.SaveVehicleCommand.ExecuteAsync(null);

        await WaitUntil(() => vm.HasVehicle);
        vm.CanRegisterVehicle.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Teşhisi kaydet ----

    [Fact]
    public async Task SaveDiagnosis_Is_Disabled_Without_Known_Vehicle()
    {
        _workshop.Setup(w => w.FindVehicleByVinAsync(Vin, It.IsAny<CancellationToken>())).ReturnsAsync((Vehicle?)null);
        var vm = await ConnectedAsync();

        vm.SaveDiagnosisCommand.CanExecute(null).Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveDiagnosis_Is_Disabled_When_Not_Connected()
    {
        var vm = Create();
        vm.VinInput = Vin;
        await vm.FindVehicleCommand.ExecuteAsync(null);

        vm.HasVehicle.Should().BeTrue();
        vm.SaveDiagnosisCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SaveDiagnosis_Stores_Codes_Snapshot_Note_And_Mileage()
    {
        RepairRecord? saved = null;
        _workshop.Setup(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()))
            .Callback<RepairRecord, CancellationToken>((r, _) => saved = r)
            .ReturnsAsync((RepairRecord r, CancellationToken _) => r);
        var vm = await ConnectedAsync();
        await WaitUntil(() => vm.RpmText != "—");
        await vm.ReadFaultsCommand.ExecuteAsync(null);
        vm.RepairNote = "  Hava akış sensörü temizlendi  ";
        vm.RepairMileage = "182.500";

        vm.SaveDiagnosisCommand.CanExecute(null).Should().BeTrue();
        await vm.SaveDiagnosisCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.VehicleId.Should().Be(7);
        saved.Description.Should().Be("Hava akış sensörü temizlendi");
        saved.Mileage.Should().Be(182_500);
        saved.DtcRecords.Select(d => d.Code).Should().Equal("P0104", "P0301");
        saved.DtcRecords.Should().OnlyContain(d => !d.WasCleared);
        saved.LiveDataSnapshots.Should().ContainSingle().Which.Rpm.Should().Be(1726);
        vm.RepairNote.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("kaydedildi");
        vm.IsStatusError.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveDiagnosis_Marks_Codes_As_Cleared_After_Clearing()
    {
        RepairRecord? saved = null;
        _workshop.Setup(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()))
            .Callback<RepairRecord, CancellationToken>((r, _) => saved = r)
            .ReturnsAsync((RepairRecord r, CancellationToken _) => r);
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var vm = await ConnectedAsync();
        await vm.ReadFaultsCommand.ExecuteAsync(null);
        await vm.ClearFaultsCommand.ExecuteAsync(null);

        await vm.SaveDiagnosisCommand.ExecuteAsync(null);

        saved!.DtcRecords.Select(d => d.Code).Should().Equal("P0104", "P0301");
        saved.DtcRecords.Should().OnlyContain(d => d.WasCleared);
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveDiagnosis_Without_Note_Uses_Default_Description()
    {
        RepairRecord? saved = null;
        _workshop.Setup(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()))
            .Callback<RepairRecord, CancellationToken>((r, _) => saved = r)
            .ReturnsAsync((RepairRecord r, CancellationToken _) => r);
        var vm = await ConnectedAsync();

        await vm.SaveDiagnosisCommand.ExecuteAsync(null);

        saved!.Description.Should().Contain("Teşhis");
        saved.DtcRecords.Should().BeEmpty(); // arıza kodu okunmadı
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveDiagnosis_With_Bad_Mileage_Shows_Error_And_Saves_Nothing()
    {
        var vm = await ConnectedAsync();
        vm.RepairMileage = "abc";

        await vm.SaveDiagnosisCommand.ExecuteAsync(null);

        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Contain("Kilometre");
        _workshop.Verify(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task SaveDiagnosis_Database_Error_Is_Shown_In_Plain_Turkish()
    {
        _workshop.Setup(w => w.AddRepairAsync(It.IsAny<RepairRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DataValidationException("Tamir kaydı bir araca bağlı olmalı."));
        var vm = await ConnectedAsync();

        await vm.SaveDiagnosisCommand.ExecuteAsync(null);

        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Be("Tamir kaydı bir araca bağlı olmalı.");
        vm.IsBusy.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Gerçek sahte cihaz + gerçek veritabanı ----

    [Fact]
    public async Task Fake_Faulty_Car_Is_Recognized_From_Real_Database()
    {
        using var db = new InMemoryDatabase();
        var customer = await db.Service.SaveCustomerAsync(new Customer { FullName = "Veli Kaya" });
        await db.Service.SaveVehicleAsync(new Vehicle
        {
            CustomerId = customer.Id, Vin = FakeElmTransport.FaultyVehicleVin, Plate = "23 XYZ 789", Make = "Mercedes", Model = "E200"
        });

        var customersVm = new CustomersViewModel(db.Service, _dialogs.Object);
        var vm = new MainViewModel(_ports.Object, new ObdServiceFactory(), _dialogs.Object, db.Service, customersVm)
        { PollInterval = TimeSpan.FromMilliseconds(20) };
        vm.SelectedScenario = vm.Scenarios.First(s => s.Value == FakeScenario.Faulty);

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.VinInput.Should().Be(FakeElmTransport.FaultyVehicleVin);
        vm.VehicleInfoText.Should().Contain("23 XYZ 789").And.Contain("Veli Kaya");

        await vm.ReadFaultsCommand.ExecuteAsync(null);
        vm.RepairNote = "Test kaydı";
        await vm.SaveDiagnosisCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);

        var history = await db.Service.GetRepairHistoryAsync(vm.CurrentVehicle!.Id);
        history.Should().ContainSingle();
        history[0].DtcRecords.Select(d => d.Code).Should().BeEquivalentTo("P0104", "P0301");
        history[0].LiveDataSnapshots.Should().ContainSingle();
    }

    [Fact]
    public async Task Fake_Normal_Car_Not_In_Database_Offers_Registration()
    {
        using var db = new InMemoryDatabase();
        var vm = new MainViewModel(_ports.Object, new ObdServiceFactory(), _dialogs.Object, db.Service,
            new CustomersViewModel(db.Service, _dialogs.Object));

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.VinInput.Should().Be(FakeElmTransport.NormalVehicleVin);
        vm.CanRegisterVehicle.Should().BeTrue();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }
}
