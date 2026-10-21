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

    /// <summary>Rölantideki aracın değerlerini verilen saniyede okur.</summary>
    private static async Task<(double Rpm, int Speed, int Temp, double Throttle)> ReadIdleAt(double seconds)
    {
        var fake = new FakeElmTransport(FakeScenario.Idle, () => TimeSpan.FromSeconds(seconds));
        await fake.ConnectAsync();
        await fake.SendCommandAsync("ATE0");

        PidParser.TryParseRpm(await fake.SendCommandAsync("010C"), out var rpm).Should().BeTrue();
        PidParser.TryParseSpeed(await fake.SendCommandAsync("010D"), out var speed).Should().BeTrue();
        PidParser.TryParseCoolantTemp(await fake.SendCommandAsync("0105"), out var temp).Should().BeTrue();
        PidParser.TryParseThrottle(await fake.SendCommandAsync("0111"), out var throttle).Should().BeTrue();
        return (rpm, speed, temp, throttle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13.7)]
    [InlineData(60)]
    [InlineData(400)]
    public async Task Idle_Vehicle_Stands_Still_With_Released_Pedal(double seconds)
    {
        var (rpm, speed, _, throttle) = await ReadIdleAt(seconds);

        speed.Should().Be(0);
        rpm.Should().BeInRange(750, 1100);
        throttle.Should().BeInRange(0, 4);
    }

    [Fact]
    public async Task Idle_Rpm_Drops_As_Engine_Warms_Up()
    {
        var cold = await ReadIdleAt(0);     // 30 °C
        var warm = await ReadIdleAt(400);   // 90 °C

        cold.Temp.Should().Be(30);
        warm.Temp.Should().Be(90);
        cold.Rpm.Should().BeGreaterThan(warm.Rpm + 150);
        warm.Rpm.Should().BeInRange(750, 850);
    }

    [Fact]
    public async Task Idle_Vehicle_Has_No_Faults_And_Normal_Vin()
    {
        var fake = await ReadyAsync(FakeScenario.Idle);

        DtcParser.TryParse(await fake.SendCommandAsync("03"), out var codes).Should().BeTrue();
        codes.Should().BeEmpty();
        VinParser.TryParse(await fake.SendCommandAsync("0902"), out var vin).Should().BeTrue();
        vin.Should().Be(FakeElmTransport.NormalVehicleVin);
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

    [Theory]
    [InlineData(FakeScenario.Normal, FakeElmTransport.NormalVehicleVin)]
    [InlineData(FakeScenario.Slow, FakeElmTransport.NormalVehicleVin)]
    [InlineData(FakeScenario.Faulty, FakeElmTransport.FaultyVehicleVin)]
    public async Task Vin_From_Fake_Is_Parsed(FakeScenario scenario, string expectedVin)
    {
        var fake = await ReadyAsync(scenario);

        VinParser.TryParse(await fake.SendCommandAsync("0902"), out var vin).Should().BeTrue();

        vin.Should().Be(expectedVin);
        VinValidator.IsValid(vin).Should().BeTrue();
    }

    [Fact]
    public async Task Vin_Response_Has_Can_Multi_Frame_Shape_With_Spaces()
    {
        var fake = new FakeElmTransport(FakeScenario.Normal);
        await fake.ConnectAsync();
        await fake.SendCommandAsync("ATE0");
        await fake.SendCommandAsync("0100"); // SEARCHING satırı atlansın

        (await fake.SendCommandAsync("0902")).Should().Be(
            "014\r0:49 02 01 57 56 57\r1:5A 5A 5A 31 4A 5A 58\r2:57 30 30 30 30 30 31\r\r>");
    }

    [Fact]
    public async Task Custom_Vin_Can_Be_Given()
    {
        var fake = new FakeElmTransport(FakeScenario.Normal, vin: "1HGCM82633A004352");
        await fake.ConnectAsync();

        VinParser.TryParse(await fake.SendCommandAsync("0902"), out var vin).Should().BeTrue();
        vin.Should().Be("1HGCM82633A004352");
    }

    [Fact]
    public async Task NotConnected_Vehicle_Gives_No_Vin()
    {
        var fake = await ReadyAsync(FakeScenario.NotConnected);
        VinParser.TryParse(await fake.SendCommandAsync("0902"), out _).Should().BeFalse();
    }

    [Fact]
    public async Task NotConnected_Vehicle_Gives_No_Values()
    {
        var fake = await ReadyAsync(FakeScenario.NotConnected);

        PidParser.TryParseRpm(await fake.SendCommandAsync("010C"), out _).Should().BeFalse();
        DtcParser.TryParse(await fake.SendCommandAsync("03"), out _).Should().BeFalse();
    }
}
