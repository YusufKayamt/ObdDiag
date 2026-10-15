using FluentAssertions;
using ObdDiag.Hardware;

namespace ObdDiag.Hardware.Tests;

public class FakeElmTransportTests
{
    // Testlerde zamanı elle ilerletmek için sahte saat.
    private TimeSpan _now = TimeSpan.Zero;

    private async Task<FakeElmTransport> CreateAsync(FakeScenario scenario = FakeScenario.Normal, TimeSpan? slowDelay = null)
    {
        var fake = new FakeElmTransport(scenario, () => _now, slowDelay);
        await fake.ConnectAsync();
        return fake;
    }

    /// <summary>Gerçek kurulum sırası: ATZ, ATE0, ATL0, ATS0, ATH0, ATSP0.</summary>
    private static async Task InitAsync(FakeElmTransport fake)
    {
        foreach (var cmd in new[] { "ATZ", "ATE0", "ATL0", "ATS0", "ATH0", "ATSP0" })
            await fake.SendCommandAsync(cmd);
    }

    [Fact]
    public async Task Connect_And_Disconnect_Change_IsConnected()
    {
        var fake = new FakeElmTransport();
        fake.IsConnected.Should().BeFalse();

        await fake.ConnectAsync();
        fake.IsConnected.Should().BeTrue();

        await fake.DisconnectAsync();
        fake.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SendCommand_Without_Connect_Throws()
    {
        var fake = new FakeElmTransport();
        var act = () => fake.SendCommandAsync("ATZ");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(FakeScenario.Normal)]
    [InlineData(FakeScenario.Faulty)]
    [InlineData(FakeScenario.NotConnected)]
    [InlineData(FakeScenario.Slow)]
    public async Task Every_Response_Ends_With_Prompt(FakeScenario scenario)
    {
        var fake = await CreateAsync(scenario, TimeSpan.Zero);
        foreach (var cmd in new[] { "ATZ", "ATE0", "010C", "03", "ZZZ", "AT?" })
            (await fake.SendCommandAsync(cmd)).Should().EndWith("\r\r>");
    }

    [Fact]
    public async Task Atz_Returns_Version()
    {
        var fake = await CreateAsync();
        (await fake.SendCommandAsync("ATZ")).Should().Contain("ELM327 v1.5");
    }

    [Fact]
    public async Task Echo_Is_On_By_Default_And_Off_After_Ate0()
    {
        var fake = await CreateAsync();
        (await fake.SendCommandAsync("ATI")).Should().StartWith("ATI\r");

        await fake.SendCommandAsync("ATE0");
        (await fake.SendCommandAsync("ATI")).Should().Be("ELM327 v1.5\r\r>");
    }

    [Fact]
    public async Task Ats0_Removes_Spaces_From_Data()
    {
        var fake = await CreateAsync();
        await fake.SendCommandAsync("ATE0");
        await fake.SendCommandAsync("0100"); // protokol arama satırı bir kere gelsin

        (await fake.SendCommandAsync("0105")).Should().MatchRegex(@"^41 05 [0-9A-F]{2}\r\r>$");

        await fake.SendCommandAsync("ATS0");
        (await fake.SendCommandAsync("0105")).Should().MatchRegex(@"^4105[0-9A-F]{2}\r\r>$");
    }

    [Fact]
    public async Task First_Obd_Command_Shows_Searching_Only_Once()
    {
        var fake = await CreateAsync();
        await InitAsync(fake);

        (await fake.SendCommandAsync("010D")).Should().StartWith("SEARCHING...\r");
        (await fake.SendCommandAsync("010D")).Should().NotContain("SEARCHING");
    }

    [Fact]
    public async Task Unknown_Command_Returns_QuestionMark()
    {
        var fake = await CreateAsync();
        await InitAsync(fake);
        (await fake.SendCommandAsync("HELLO")).Should().Be("?\r\r>");
        (await fake.SendCommandAsync("ATXYZ")).Should().Be("?\r\r>");
    }

    [Fact]
    public async Task Unsupported_Pid_Returns_NoData()
    {
        var fake = await CreateAsync();
        await InitAsync(fake);
        await fake.SendCommandAsync("0100");
        (await fake.SendCommandAsync("01FF")).Should().Be("NO DATA\r\r>");
    }

    [Fact]
    public async Task Supported_Pids_Response_Is_Correct()
    {
        var fake = await CreateAsync();
        await InitAsync(fake);
        await fake.SendCommandAsync("0100"); // SEARCHING satırı atlansın
        (await fake.SendCommandAsync("0100")).Should().Be("410018188000\r\r>");
    }

    // ---- Canlı veri ----

    private static byte[] DataBytes(string response, int skip)
    {
        // "410C1AF8\r\r>" -> 1A F8 baytları (ilk 'skip' bayt: mod + PID)
        var hex = response.Replace("SEARCHING...", "").Replace("\r", "").Replace(">", "").Trim();
        return Enumerable.Range(0, hex.Length / 2)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
            .Skip(skip).ToArray();
    }

    private async Task<FakeElmTransport> ReadyAsync(FakeScenario scenario = FakeScenario.Normal)
    {
        var fake = await CreateAsync(scenario, TimeSpan.Zero);
        await InitAsync(fake);
        await fake.SendCommandAsync("0100"); // SEARCHING bir kere gelsin
        return fake;
    }

    [Fact]
    public async Task Rpm_Is_In_Realistic_Range_And_Changes_Over_Time()
    {
        var fake = await ReadyAsync();
        var values = new List<double>();
        for (var s = 0; s < 40; s += 2)
        {
            _now = TimeSpan.FromSeconds(s);
            var b = DataBytes(await fake.SendCommandAsync("010C"), 2);
            values.Add((256 * b[0] + b[1]) / 4.0);
        }

        values.Should().OnlyContain(v => v >= 600 && v <= 4000);
        values.Distinct().Count().Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task Speed_Is_In_Realistic_Range_And_Changes_Over_Time()
    {
        var fake = await ReadyAsync();
        var values = new List<int>();
        for (var s = 0; s < 40; s += 2)
        {
            _now = TimeSpan.FromSeconds(s);
            values.Add(DataBytes(await fake.SendCommandAsync("010D"), 2)[0]);
        }

        values.Should().OnlyContain(v => v >= 0 && v <= 130);
        values.Distinct().Count().Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task Coolant_Warms_Up_Then_Stays_At_90()
    {
        var fake = await ReadyAsync();

        _now = TimeSpan.Zero;
        (DataBytes(await fake.SendCommandAsync("0105"), 2)[0] - 40).Should().Be(30);

        _now = TimeSpan.FromMinutes(10);
        (DataBytes(await fake.SendCommandAsync("0105"), 2)[0] - 40).Should().Be(90);
    }

    [Fact]
    public async Task Throttle_Is_Valid_Percentage()
    {
        var fake = await ReadyAsync();
        for (var s = 0; s < 40; s += 3)
        {
            _now = TimeSpan.FromSeconds(s);
            var pct = DataBytes(await fake.SendCommandAsync("0111"), 2)[0] * 100 / 255.0;
            pct.Should().BeInRange(0, 100);
        }
    }

    // ---- Arıza kodları ----

    [Fact]
    public async Task Normal_Vehicle_Has_No_Fault_Codes()
    {
        var fake = await ReadyAsync(FakeScenario.Normal);
        (await fake.SendCommandAsync("03")).Should().Be("4300\r\r>");
    }

    [Fact]
    public async Task Faulty_Vehicle_Returns_P0104_And_P0301()
    {
        var fake = await ReadyAsync(FakeScenario.Faulty);
        // CAN biçimi: 43, kod sayısı (02), 0104 (P0104), 0301 (P0301)
        (await fake.SendCommandAsync("03")).Should().Be("43020104" + "0301" + "\r\r>");
    }

    [Fact]
    public async Task Clearing_Fault_Codes_Removes_Them()
    {
        var fake = await ReadyAsync(FakeScenario.Faulty);

        (await fake.SendCommandAsync("04")).Should().Be("44\r\r>");
        (await fake.SendCommandAsync("03")).Should().Be("4300\r\r>");
    }

    // ---- Diğer senaryolar ----

    [Fact]
    public async Task NotConnected_Vehicle_Returns_UnableToConnect()
    {
        var fake = await CreateAsync(FakeScenario.NotConnected);
        await InitAsync(fake); // AT komutları cihazla ilgili, çalışır

        (await fake.SendCommandAsync("0100")).Should().Be("SEARCHING...\rUNABLE TO CONNECT\r\r>");
        (await fake.SendCommandAsync("010C")).Should().Be("SEARCHING...\rUNABLE TO CONNECT\r\r>");
    }

    [Fact]
    public async Task Slow_Vehicle_Waits_Before_Answering()
    {
        var fake = await CreateAsync(FakeScenario.Slow, TimeSpan.FromMilliseconds(300));

        var started = DateTime.UtcNow;
        await fake.SendCommandAsync("ATZ");
        (DateTime.UtcNow - started).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Slow_Vehicle_Can_Be_Cancelled()
    {
        var fake = await CreateAsync(FakeScenario.Slow, TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => fake.SendCommandAsync("ATZ", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Concurrent_Commands_Are_Handled_One_At_A_Time()
    {
        var fake = await CreateAsync(FakeScenario.Slow, TimeSpan.FromMilliseconds(150));

        var started = DateTime.UtcNow;
        await Task.WhenAll(fake.SendCommandAsync("ATI"), fake.SendCommandAsync("ATI"));

        // İki komut art arda işlenmeli: en az 2 x 150 ms
        (DateTime.UtcNow - started).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(280));
    }
}
