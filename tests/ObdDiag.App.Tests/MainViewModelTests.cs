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

public class MainViewModelTests
{
    private readonly Mock<IComPortProvider> _ports = new();
    private readonly Mock<IObdServiceFactory> _factory = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly Mock<IObdService> _service = new();
    private readonly Mock<IWorkshopService> _workshop = new();

    private ConnectionSettings? _lastSettings;

    public MainViewModelTests()
    {
        _workshop.Setup(w => w.GetCustomersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Customer>());
        _ports.Setup(p => p.GetPortNames()).Returns(new[] { "COM3", "COM4" });
        _factory.Setup(f => f.Create(It.IsAny<ConnectionSettings>()))
            .Callback<ConnectionSettings>(s => _lastSettings = s)
            .Returns(_service.Object);

        _service.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _service.Setup(s => s.DisconnectAsync()).Returns(Task.CompletedTask);
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveData(1726, 60, 83, 50));
        _service.Setup(s => s.ReadFaultCodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "P0104", "P0301" });
        _service.Setup(s => s.ClearFaultCodesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private MainViewModel Create(IObdServiceFactory? factory = null) =>
        new(_ports.Object, factory ?? _factory.Object, _dialogs.Object, _workshop.Object,
            new CustomersViewModel(_workshop.Object, _dialogs.Object))
        {
            PollInterval = TimeSpan.FromMilliseconds(10)
        };

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Beklenen durum 5 saniyede oluşmadı.");
            await Task.Delay(10);
        }
    }

    private async Task<MainViewModel> ConnectedAsync()
    {
        var vm = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        return vm;
    }

    // ---- Başlangıç ----

    [Fact]
    public void Starts_With_Fake_Device_And_Lists_Ports()
    {
        var vm = Create();

        vm.UseFakeDevice.Should().BeTrue();
        vm.Ports.Should().Equal("COM3", "COM4");
        vm.SelectedPort.Should().Be("COM3");
        vm.Scenarios.Should().HaveCount(4);
        vm.IsConnected.Should().BeFalse();
        vm.RpmText.Should().Be("—");
        vm.ConnectCommand.CanExecute(null).Should().BeTrue();
        vm.DisconnectCommand.CanExecute(null).Should().BeFalse();
        vm.ReadFaultsCommand.CanExecute(null).Should().BeFalse();
        vm.ClearFaultsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Real_Device_Needs_A_Port()
    {
        _ports.Setup(p => p.GetPortNames()).Returns(Array.Empty<string>());
        var vm = Create();

        vm.UseFakeDevice = false;

        vm.SelectedPort.Should().BeNull();
        vm.ConnectCommand.CanExecute(null).Should().BeFalse();
        vm.CanSelectPort.Should().BeTrue();
        vm.CanSelectScenario.Should().BeFalse();
    }

    [Fact]
    public void RefreshPorts_Keeps_Selection_If_Still_Present()
    {
        var vm = Create();
        vm.SelectedPort = "COM4";
        _ports.Setup(p => p.GetPortNames()).Returns(new[] { "COM4", "COM7" });

        vm.RefreshPortsCommand.Execute(null);

        vm.Ports.Should().Equal("COM4", "COM7");
        vm.SelectedPort.Should().Be("COM4");
    }

    // ---- Bağlanma ----

    [Fact]
    public async Task Connect_Succeeds_And_Shows_Live_Data()
    {
        var vm = await ConnectedAsync();

        vm.IsConnected.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
        vm.IsStatusError.Should().BeFalse();
        vm.CanEditConnection.Should().BeFalse();
        vm.DisconnectCommand.CanExecute(null).Should().BeTrue();
        vm.ConnectCommand.CanExecute(null).Should().BeFalse();

        await WaitUntil(() => vm.RpmText != "—");
        vm.RpmText.Should().Be("1726");
        vm.SpeedText.Should().Be("60");
        vm.CoolantText.Should().Be("83");
        vm.ThrottleText.Should().Be("50");
        vm.RpmValue.Should().Be(1726);

        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Connect_Passes_Selected_Options_To_Factory()
    {
        var vm = Create();
        vm.SelectedScenario = vm.Scenarios.First(s => s.Value == FakeScenario.Faulty);

        await vm.ConnectCommand.ExecuteAsync(null);

        _lastSettings.Should().Be(new ConnectionSettings(true, "COM3", FakeScenario.Faulty));
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Connect_Failure_Shows_Friendly_Error_And_Stays_Disconnected()
    {
        _service.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObdException("Araca bağlanılamadı."));
        var vm = Create();

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.IsConnected.Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Be("Araca bağlanılamadı.");
        vm.ConnectCommand.CanExecute(null).Should().BeTrue(); // tekrar denenebilir
        _service.Verify(s => s.DisconnectAsync(), Times.Once);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "Port açılamadı")]
    [InlineData(typeof(IOException), "Cihaza ulaşılamadı")]
    [InlineData(typeof(TimeoutException), "zaman")]
    public async Task Technical_Errors_Become_Plain_Turkish(Type exceptionType, string expectedPart)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "zaman aşımı")!;
        _service.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        var vm = Create();

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Contain(expectedPart);
    }

    [Fact]
    public async Task Busy_Flag_Is_On_While_Connecting()
    {
        var gate = new TaskCompletionSource();
        _service.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(gate.Task);
        var vm = Create();

        var connecting = vm.ConnectCommand.ExecuteAsync(null);

        vm.IsBusy.Should().BeTrue();
        vm.CanEditConnection.Should().BeFalse();
        vm.ConnectCommand.CanExecute(null).Should().BeFalse();

        gate.SetResult();
        await connecting;
        vm.IsBusy.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Bağlantıyı kesme ----

    [Fact]
    public async Task Disconnect_Resets_Everything()
    {
        var vm = await ConnectedAsync();
        await WaitUntil(() => vm.RpmText != "—");

        await vm.DisconnectCommand.ExecuteAsync(null);

        vm.IsConnected.Should().BeFalse();
        vm.RpmText.Should().Be("—");
        vm.SpeedText.Should().Be("—");
        vm.RpmValue.Should().Be(0);
        vm.CanEditConnection.Should().BeTrue();
        _service.Verify(s => s.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task Repeated_Polling_Failures_Disconnect_With_Error()
    {
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObdException("Cevap yok."));
        var vm = await ConnectedAsync();

        await WaitUntil(() => !vm.IsConnected);

        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Contain("Bağlantı koptu");
        _service.Verify(s => s.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task Single_Polling_Failure_Does_Not_Disconnect()
    {
        var calls = 0;
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? Task.FromException<LiveData>(new ObdException("Geçici hata"))
                : Task.FromResult(new LiveData(1000, 10, 80, 5)));
        var vm = await ConnectedAsync();

        await WaitUntil(() => vm.RpmText == "1000");

        vm.IsConnected.Should().BeTrue();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task All_Null_Live_Data_Counts_As_Failure()
    {
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveData(null, null, null, null));
        var vm = await ConnectedAsync();

        await WaitUntil(() => !vm.IsConnected);

        vm.IsStatusError.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_Single_Value_Shows_Dash()
    {
        _service.Setup(s => s.ReadLiveDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveData(900, null, 70, null));
        var vm = await ConnectedAsync();

        await WaitUntil(() => vm.RpmText == "900");

        vm.SpeedText.Should().Be("—");
        vm.ThrottleText.Should().Be("—");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Arıza kodları ----

    [Fact]
    public async Task ReadFaults_Lists_Codes()
    {
        var vm = await ConnectedAsync();

        await vm.ReadFaultsCommand.ExecuteAsync(null);

        vm.FaultCodes.Should().Equal("P0104", "P0301");
        vm.FaultsSummary.Should().Contain("2 arıza kodu");
        vm.IsBusy.Should().BeFalse();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ReadFaults_With_No_Codes_Says_So()
    {
        _service.Setup(s => s.ReadFaultCodesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        var vm = await ConnectedAsync();

        await vm.ReadFaultsCommand.ExecuteAsync(null);

        vm.FaultCodes.Should().BeEmpty();
        vm.FaultsSummary.Should().Contain("bulunamadı");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ReadFaults_Error_Is_Shown_Without_Disconnecting()
    {
        _service.Setup(s => s.ReadFaultCodesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObdException("Arıza kodları okunamadı."));
        var vm = await ConnectedAsync();

        await vm.ReadFaultsCommand.ExecuteAsync(null);

        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Be("Arıza kodları okunamadı.");
        vm.IsConnected.Should().BeTrue();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ClearFaults_Asks_For_Confirmation_And_Does_Nothing_If_Declined()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var vm = await ConnectedAsync();
        await vm.ReadFaultsCommand.ExecuteAsync(null);

        await vm.ClearFaultsCommand.ExecuteAsync(null);

        _dialogs.Verify(d => d.Confirm(It.IsAny<string>(), It.Is<string>(m => m.Contains("geri alınamaz"))), Times.Once);
        _service.Verify(s => s.ClearFaultCodesAsync(It.IsAny<CancellationToken>()), Times.Never);
        vm.FaultCodes.Should().HaveCount(2);
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ClearFaults_Clears_List_When_Approved()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var vm = await ConnectedAsync();
        await vm.ReadFaultsCommand.ExecuteAsync(null);

        await vm.ClearFaultsCommand.ExecuteAsync(null);

        _service.Verify(s => s.ClearFaultCodesAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.FaultCodes.Should().BeEmpty();
        vm.FaultsSummary.Should().Contain("silindi");
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ClearFaults_Error_Keeps_List_And_Shows_Message()
    {
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _service.Setup(s => s.ClearFaultCodesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObdException("Arıza kodları silinemedi."));
        var vm = await ConnectedAsync();
        await vm.ReadFaultsCommand.ExecuteAsync(null);

        await vm.ClearFaultsCommand.ExecuteAsync(null);

        vm.FaultCodes.Should().HaveCount(2);
        vm.IsStatusError.Should().BeTrue();
        await vm.DisconnectCommand.ExecuteAsync(null);
    }

    // ---- Kapanış ----

    [Fact]
    public async Task Shutdown_Releases_Device()
    {
        var vm = await ConnectedAsync();

        vm.Shutdown();

        _service.Verify(s => s.DisconnectAsync(), Times.Once);
    }

    // ---- Gerçek sahte cihazla uçtan uca ----

    [Fact]
    public async Task End_To_End_With_Real_Fake_Device()
    {
        var vm = Create(new ObdServiceFactory());
        vm.PollInterval = TimeSpan.FromMilliseconds(20);
        _dialogs.Setup(d => d.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        vm.SelectedScenario = vm.Scenarios.First(s => s.Value == FakeScenario.Faulty);

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.IsConnected.Should().BeTrue();
        await WaitUntil(() => vm.RpmText != "—");

        await vm.ReadFaultsCommand.ExecuteAsync(null);
        vm.FaultCodes.Should().Equal("P0104", "P0301");

        await vm.ClearFaultsCommand.ExecuteAsync(null);
        await vm.ReadFaultsCommand.ExecuteAsync(null);
        vm.FaultCodes.Should().BeEmpty();

        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task End_To_End_NotConnected_Vehicle_Shows_Error()
    {
        var vm = Create(new ObdServiceFactory());
        vm.SelectedScenario = vm.Scenarios.First(s => s.Value == FakeScenario.NotConnected);

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.IsConnected.Should().BeFalse();
        vm.IsStatusError.Should().BeTrue();
        vm.StatusMessage.Should().Contain("Araca bağlanılamadı");
    }
}
