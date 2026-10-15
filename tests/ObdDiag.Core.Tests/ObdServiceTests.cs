using FluentAssertions;
using Moq;
using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Models;
using ObdDiag.Core.Services;

namespace ObdDiag.Core.Tests;

public class ObdServiceTests
{
    /// <summary>Komut -> cevap tablosuyla çalışan sahte cihaz; gönderilen komutları kaydeder.</summary>
    private sealed class Setup
    {
        public Mock<IElmTransport> Transport { get; } = new();
        public List<string> Sent { get; } = new();
        public Dictionary<string, string> Answers { get; } = new()
        {
            ["ATZ"] = "ELM327 v1.5\r\r>",
            ["ATE0"] = "OK\r\r>",
            ["ATL0"] = "OK\r\r>",
            ["ATS0"] = "OK\r\r>",
            ["ATH0"] = "OK\r\r>",
            ["ATSP0"] = "OK\r\r>",
            ["0100"] = "SEARCHING...\r4100BE1FA813\r\r>",
        };

        public Setup()
        {
            var connected = false;
            Transport.Setup(t => t.IsConnected).Returns(() => connected);
            Transport.Setup(t => t.ConnectAsync(It.IsAny<CancellationToken>()))
                .Callback(() => connected = true).Returns(Task.CompletedTask);
            Transport.Setup(t => t.DisconnectAsync())
                .Callback(() => connected = false).Returns(Task.CompletedTask);
            Transport.Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string cmd, CancellationToken _) =>
                {
                    Sent.Add(cmd);
                    return Answers.TryGetValue(cmd, out var a) ? a : "NO DATA\r\r>";
                });
        }

        public ObdService Service => new(Transport.Object);
    }

    [Fact]
    public async Task Initialize_Sends_Init_Commands_In_Order_Then_Probes_Vehicle()
    {
        var s = new Setup();
        var service = s.Service;

        await service.InitializeAsync();

        s.Sent.Should().Equal("ATZ", "ATE0", "ATL0", "ATS0", "ATH0", "ATSP0", "0100");
        service.IsInitialized.Should().BeTrue();
        s.Transport.Verify(t => t.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Initialize_Does_Not_Reconnect_If_Already_Connected()
    {
        var s = new Setup();
        await s.Transport.Object.ConnectAsync();

        await s.Service.InitializeAsync();

        s.Transport.Verify(t => t.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Initialize_Fails_With_Friendly_Message_When_Vehicle_Not_Connected()
    {
        var s = new Setup();
        s.Answers["0100"] = "SEARCHING...\rUNABLE TO CONNECT\r\r>";
        var service = s.Service;

        var act = () => service.InitializeAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*Araca bağlanılamadı*");
        service.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_Fails_When_Vehicle_Says_NoData()
    {
        var s = new Setup();
        s.Answers["0100"] = "NO DATA\r\r>";

        var act = () => s.Service.InitializeAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*Araç cevap vermedi*");
    }

    [Fact]
    public async Task Initialize_Fails_When_Device_Does_Not_Understand_A_Command()
    {
        var s = new Setup();
        s.Answers["ATL0"] = "?\r\r>";

        var act = () => s.Service.InitializeAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*ATL0*");
        s.Sent.Should().NotContain("0100"); // hata sonrası devam edilmez
    }

    [Fact]
    public async Task Initialize_Accepts_Echo_On_Responses()
    {
        var s = new Setup();
        s.Answers["0100"] = "0100\r41 00 BE 1F A8 13\r\r>";

        await s.Service.InitializeAsync();
    }

    [Fact]
    public async Task ReadLiveData_Returns_All_Values()
    {
        var s = new Setup();
        s.Answers["010C"] = "410C1AF8\r\r>";
        s.Answers["010D"] = "410D3C\r\r>";
        s.Answers["0105"] = "41057B\r\r>";
        s.Answers["0111"] = "4111FF\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        var data = await service.ReadLiveDataAsync();

        data.Rpm.Should().Be(1726);
        data.Speed.Should().Be(60);
        data.CoolantTemp.Should().Be(83);
        data.Throttle.Should().Be(100);
    }

    [Fact]
    public async Task ReadLiveData_Gives_Null_For_Unavailable_Values()
    {
        var s = new Setup();
        s.Answers["010C"] = "410C1AF8\r\r>";
        s.Answers["010D"] = "NO DATA\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        var data = await service.ReadLiveDataAsync();

        data.Rpm.Should().Be(1726);
        data.Speed.Should().BeNull();
        data.CoolantTemp.Should().BeNull(); // tabloda yok -> NO DATA
        data.Throttle.Should().BeNull();
    }

    [Fact]
    public async Task ReadFaultCodes_Returns_Codes()
    {
        var s = new Setup();
        s.Answers["03"] = "43020104 0301\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        (await service.ReadFaultCodesAsync()).Should().Equal("P0104", "P0301");
    }

    [Fact]
    public async Task ReadFaultCodes_Returns_Empty_List_When_No_Faults()
    {
        var s = new Setup();
        s.Answers["03"] = "4300\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        (await service.ReadFaultCodesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadFaultCodes_Throws_Friendly_Error_On_Garbage()
    {
        var s = new Setup();
        s.Answers["03"] = "NO DATA\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        var act = () => service.ReadFaultCodesAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*okunamadı*");
    }

    [Fact]
    public async Task ClearFaultCodes_Succeeds_On_44()
    {
        var s = new Setup();
        s.Answers["04"] = "44\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        await service.ClearFaultCodesAsync();

        s.Sent.Should().EndWith("04");
    }

    [Fact]
    public async Task ClearFaultCodes_Throws_When_Not_Confirmed()
    {
        var s = new Setup();
        s.Answers["04"] = "NO DATA\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        var act = () => service.ClearFaultCodesAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*silinemedi*");
    }

    [Fact]
    public async Task Reading_Before_Initialize_Throws()
    {
        var service = new Setup().Service;

        await ((Func<Task>)(() => service.ReadLiveDataAsync())).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => service.ReadFaultCodesAsync())).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => service.ClearFaultCodesAsync())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Disconnect_Resets_Initialized_Flag()
    {
        var s = new Setup();
        var service = s.Service;
        await service.InitializeAsync();

        await service.DisconnectAsync();

        service.IsInitialized.Should().BeFalse();
        s.Transport.Verify(t => t.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task ReadVin_Returns_Vin_When_Vehicle_Supports_It()
    {
        var s = new Setup();
        s.Answers["0902"] = "014\r0:490201575657\r1:5A5A5A314A5A58\r2:57303030303031\r\r>";
        var service = s.Service;
        await service.InitializeAsync();

        (await service.ReadVinAsync()).Should().Be("WVWZZZ1JZXW000001");
    }

    [Fact]
    public async Task ReadVin_Returns_Null_When_Not_Supported()
    {
        var s = new Setup(); // 0902 -> NO DATA
        var service = s.Service;
        await service.InitializeAsync();

        (await service.ReadVinAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ReadVin_Before_Initialize_Throws()
    {
        var service = new Setup().Service;
        await ((Func<Task>)(() => service.ReadVinAsync())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_Token_Is_Passed_To_Transport()
    {
        var s = new Setup();
        using var cts = new CancellationTokenSource();

        await s.Service.InitializeAsync(cts.Token);

        s.Transport.Verify(t => t.SendCommandAsync(It.IsAny<string>(), cts.Token), Times.Exactly(7));
    }
}
