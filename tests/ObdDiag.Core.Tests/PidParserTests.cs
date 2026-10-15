using FluentAssertions;
using ObdDiag.Core.Parsing;

namespace ObdDiag.Core.Tests;

public class PidParserTests
{
    // ---- Motor devri (010C): (256*A + B) / 4 ----

    [Theory]
    [InlineData("41 0C 1A F8", 1726)]
    [InlineData("410C1AF8", 1726)]                  // boşluksuz
    [InlineData("41 0c 1a f8", 1726)]               // küçük harf
    [InlineData("410C1AF8\r\r>", 1726)]             // gerçek cihaz sonu
    [InlineData("010C\r410C1AF8\r\r>", 1726)]       // yankı (echo) açıkken
    [InlineData("SEARCHING...\r410C1AF8\r\r>", 1726)]
    [InlineData("BUS INIT: ...OK\r41 0C 1A F8\r\r>", 1726)]
    [InlineData("41 0C 00 00", 0)]
    [InlineData("41 0C 00 04", 1)]
    [InlineData("41 0C 03 20", 200)]
    public void Rpm_Is_Parsed(string response, double expected)
    {
        PidParser.TryParseRpm(response, out var rpm).Should().BeTrue();
        rpm.Should().Be(expected);
    }

    [Fact]
    public void Rpm_Max_Value_Is_16383_75()
    {
        PidParser.TryParseRpm("41 0C FF FF", out var rpm).Should().BeTrue();
        rpm.Should().Be(16383.75);
    }

    [Fact]
    public void Rpm_Uses_First_Answer_When_Multiple_Ecus_Reply()
    {
        PidParser.TryParseRpm("41 0C 1A F8\r41 0C 0F A0\r\r>", out var rpm).Should().BeTrue();
        rpm.Should().Be(1726);
    }

    // ---- Araç hızı (010D): A km/s ----

    [Theory]
    [InlineData("41 0D 3C", 60)]
    [InlineData("410D3C\r\r>", 60)]
    [InlineData("41 0D 00", 0)]
    [InlineData("41 0D FF", 255)]
    public void Speed_Is_Parsed(string response, int expected)
    {
        PidParser.TryParseSpeed(response, out var speed).Should().BeTrue();
        speed.Should().Be(expected);
    }

    // ---- Soğutma suyu (0105): A - 40 ----

    [Theory]
    [InlineData("41 05 7B", 83)]
    [InlineData("41057B\r\r>", 83)]
    [InlineData("41 05 00", -40)]
    [InlineData("41 05 28", 0)]
    [InlineData("41 05 FF", 215)]
    public void CoolantTemp_Is_Parsed(string response, int expected)
    {
        PidParser.TryParseCoolantTemp(response, out var temp).Should().BeTrue();
        temp.Should().Be(expected);
    }

    // ---- Gaz kelebeği (0111): A * 100 / 255 ----

    [Theory]
    [InlineData("41 11 00", 0)]
    [InlineData("41 11 FF", 100)]
    [InlineData("411180\r\r>", 50.196)]
    public void Throttle_Is_Parsed(string response, double expected)
    {
        PidParser.TryParseThrottle(response, out var throttle).Should().BeTrue();
        throttle.Should().BeApproximately(expected, 0.001);
    }

    // ---- Hatalı / boş / bozuk cevaplar ----

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("UNABLE TO CONNECT")]
    [InlineData("SEARCHING...\rUNABLE TO CONNECT\r\r>")]
    [InlineData("?")]
    [InlineData("STOPPED")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">")]
    [InlineData("41 0C")]                // veri baytı yok
    [InlineData("41 0C 1A")]             // devir için 2 bayt lazım, 1 var
    [InlineData("41 0C 1A F")]           // yarım bayt
    [InlineData("41 0C ZZ F8")]          // hex olmayan karakter
    [InlineData("41 0D 1A F8 GG")]       // sonunda çöp
    [InlineData("41 0D 3C")]             // başka PID'in cevabı
    [InlineData("7F 01 12")]             // olumsuz cevap (negative response)
    [InlineData("42 0C 1A F8")]          // yanlış mod
    public void Rpm_Returns_False_For_Bad_Input(string response)
    {
        PidParser.TryParseRpm(response, out var rpm).Should().BeFalse();
        rpm.Should().Be(0);
    }

    [Fact]
    public void Null_Input_Returns_False()
    {
        PidParser.TryParseRpm(null!, out _).Should().BeFalse();
        PidParser.TryParseSpeed(null!, out _).Should().BeFalse();
        PidParser.TryParseCoolantTemp(null!, out _).Should().BeFalse();
        PidParser.TryParseThrottle(null!, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("41 0D")]
    [InlineData("41 0C 1A F8")]          // başka PID
    [InlineData("41 0D ZZ")]
    public void Speed_Returns_False_For_Bad_Input(string response)
    {
        PidParser.TryParseSpeed(response, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("41 05")]
    [InlineData("41 0D 3C")]
    public void CoolantTemp_Returns_False_For_Bad_Input(string response)
    {
        PidParser.TryParseCoolantTemp(response, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("41 11")]
    [InlineData("41 0D 3C")]
    public void Throttle_Returns_False_For_Bad_Input(string response)
    {
        PidParser.TryParseThrottle(response, out _).Should().BeFalse();
    }
}
