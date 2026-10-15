using FluentAssertions;
using ObdDiag.Core.Parsing;
using ObdDiag.Hardware;

namespace ObdDiag.Hardware.Tests;

/// <summary>Sahte cihazın cevaplarını gerçek çözücülerle birlikte deneyen testler.</summary>
public class FakeWithParsersTests
{
    private static async Task<FakeElmTransport> ReadyAsync(FakeScenario scenario)
    {
        var fake = new FakeElmTransport(scenario, () => TimeSpan.FromSeconds(10), TimeSpan.Zero);
        await fake.ConnectAsync();
        foreach (var cmd in new[] { "ATZ", "ATE0", "ATL0", "ATS0", "ATH0", "ATSP0" })
            await fake.SendCommandAsync(cmd);
        return fake;
    }

    [Fact]
    public async Task Live_Data_From_Fake_Is_Parsed()
    {
        var fake = await ReadyAsync(FakeScenario.Normal);

        // İlk komutun cevabında "SEARCHING..." var; çözücü bunu da atlayabilmeli.
        PidParser.TryParseRpm(await fake.SendCommandAsync("010C"), out var rpm).Should().BeTrue();
        PidParser.TryParseSpeed(await fake.SendCommandAsync("010D"), out var speed).Should().BeTrue();
        PidParser.TryParseCoolantTemp(await fake.SendCommandAsync("0105"), out var temp).Should().BeTrue();
        PidParser.TryParseThrottle(await fake.SendCommandAsync("0111"), out var throttle).Should().BeTrue();

        rpm.Should().BeInRange(600, 4000);
        speed.Should().BeInRange(0, 130);
        temp.Should().Be(32); // 10. saniyede 30 + 10*0.2
        throttle.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task Faulty_Vehicle_Codes_Are_Parsed_And_Cleared()
    {
        var fake = await ReadyAsync(FakeScenario.Faulty);
        await fake.SendCommandAsync("0100");

        DtcParser.TryParse(await fake.SendCommandAsync("03"), out var codes).Should().BeTrue();
        codes.Should().Equal("P0104", "P0301");

        DtcParser.IsClearConfirmed(await fake.SendCommandAsync("04")).Should().BeTrue();

        DtcParser.TryParse(await fake.SendCommandAsync("03"), out codes).Should().BeTrue();
        codes.Should().BeEmpty();
    }

    [Fact]
    public async Task NotConnected_Vehicle_Gives_No_Values()
    {
        var fake = await ReadyAsync(FakeScenario.NotConnected);

        PidParser.TryParseRpm(await fake.SendCommandAsync("010C"), out _).Should().BeFalse();
        DtcParser.TryParse(await fake.SendCommandAsync("03"), out _).Should().BeFalse();
    }
}
