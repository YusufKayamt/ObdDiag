using FluentAssertions;
using ObdDiag.Core.Models;
using ObdDiag.Core.Services;
using ObdDiag.Hardware;

namespace ObdDiag.Hardware.Tests;

/// <summary>ObdService + sahte cihaz birlikte: arayüzün göreceği gerçek akışın denemesi.</summary>
public class ObdServiceWithFakeTests
{
    private static ObdService Create(FakeScenario scenario) =>
        new(new FakeElmTransport(scenario, () => TimeSpan.FromSeconds(10), TimeSpan.Zero));

    [Fact]
    public async Task Normal_Vehicle_Full_Flow()
    {
        var service = Create(FakeScenario.Normal);
        await service.InitializeAsync();

        var data = await service.ReadLiveDataAsync();
        data.Rpm.Should().NotBeNull().And.BeInRange(600, 4000);
        data.Speed.Should().NotBeNull();
        data.CoolantTemp.Should().Be(32);
        data.Throttle.Should().NotBeNull();

        (await service.ReadFaultCodesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Faulty_Vehicle_Codes_Can_Be_Read_And_Cleared()
    {
        var service = Create(FakeScenario.Faulty);
        await service.InitializeAsync();

        (await service.ReadFaultCodesAsync()).Should().Equal("P0104", "P0301");

        await service.ClearFaultCodesAsync();

        (await service.ReadFaultCodesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task NotConnected_Vehicle_Gives_Friendly_Error()
    {
        var service = Create(FakeScenario.NotConnected);

        var act = () => service.InitializeAsync();

        (await act.Should().ThrowAsync<ObdException>()).WithMessage("*Araca bağlanılamadı*");
    }

    [Fact]
    public async Task Slow_Vehicle_Works_And_Can_Be_Cancelled()
    {
        var slow = new ObdService(new FakeElmTransport(FakeScenario.Slow, slowDelay: TimeSpan.FromSeconds(5)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => slow.InitializeAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
