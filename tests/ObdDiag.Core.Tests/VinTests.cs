using FluentAssertions;
using ObdDiag.Core.Parsing;

namespace ObdDiag.Core.Tests;

public class VinValidatorTests
{
    [Theory]
    [InlineData("WVWZZZ1JZXW000001")]
    [InlineData("WDB2030461A123456")]
    [InlineData("1HGCM82633A004352")]
    [InlineData("wvwzzz1jzxw000001")]      // küçük harf kabul edilir
    [InlineData("  WVWZZZ1JZXW000001  ")]  // baştaki/sondaki boşluk
    [InlineData("WVW ZZZ 1JZ XW0 00001")]  // aradaki boşluklar
    public void Valid_Vins_Are_Accepted(string vin)
    {
        VinValidator.IsValid(vin).Should().BeTrue();
        VinValidator.GetError(vin).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "boş")]
    [InlineData("", "boş")]
    [InlineData("   ", "boş")]
    [InlineData("WVWZZZ1JZXW00000", "16 karakter")]       // 16
    [InlineData("WVWZZZ1JZXW0000011", "18 karakter")]     // 18
    [InlineData("ABC", "3 karakter")]
    [InlineData("WVWZZZ1JZXW00000-", "harf ve rakam")]    // özel karakter
    [InlineData("WVWZZZ1JZXW00000!", "harf ve rakam")]
    [InlineData("IVWZZZ1JZXW000001", "I, O ve Q")]        // I
    [InlineData("WVWZZZ1JZXW00000O", "I, O ve Q")]        // O
    [InlineData("QVWZZZ1JZXW000001", "I, O ve Q")]        // Q
    [InlineData("WVWZZZ1JZXW00000q", "I, O ve Q")]        // küçük q de olmaz
    public void Invalid_Vins_Are_Rejected_With_Turkish_Message(string? vin, string expectedPart)
    {
        VinValidator.IsValid(vin).Should().BeFalse();
        VinValidator.GetError(vin).Should().Contain(expectedPart);
    }

    [Fact]
    public void Normalize_Uppercases_And_Trims()
    {
        VinValidator.Normalize(" wvw zzz1jzxw000001 ").Should().Be("WVWZZZ1JZXW000001");
        VinValidator.Normalize(null).Should().BeEmpty();
    }
}

public class VinParserTests
{
    private const string Expected = "WVWZZZ1JZXW000001";

    [Fact]
    public void Can_Multi_Frame_Response()
    {
        const string raw = "014\r0: 49 02 01 57 56 57\r1: 5A 5A 5A 31 4A 5A 58\r2: 57 30 30 30 30 30 31\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Can_Without_Spaces_And_With_Echo()
    {
        const string raw = "0902\r014\r0:490201575657\r1:5A5A5A314A5A58\r2:57303030303031\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Can_Frames_In_Wrong_Order_Are_Sorted()
    {
        const string raw = "014\r2:57303030303031\r0:490201575657\r1:5A5A5A314A5A58\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Can_With_Searching_Prefix()
    {
        const string raw = "SEARCHING...\r014\r0:490201575657\r1:5A5A5A314A5A58\r2:57303030303031\r\r>";
        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Classic_Line_Per_Frame_Response()
    {
        const string raw =
            "49 02 01 00 00 00 57\r49 02 02 56 57 5A 5A\r49 02 03 5A 31 4A 5A\r" +
            "49 02 04 58 57 30 30\r49 02 05 30 30 30 31\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Classic_Duplicate_Answers_From_Two_Ecus_Are_Used_Once()
    {
        // iki kontrol ünitesi aynı satırları gönderir
        const string raw =
            "49020100000057\r49020256575A5A\r4902035A314A5A\r49020458573030\r49020530303031\r" +
            "49020100000057\r49020256575A5A\r4902035A314A5A\r49020458573030\r49020530303031\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Fact]
    public void Single_Line_Response()
    {
        // 49 02 01 + 17 ASCII bayt tek satırda
        const string raw = "490201575657 5A5A5A314A5A58 57303030303031\r\r>";

        VinParser.TryParse(raw, out var vin).Should().BeTrue();
        vin.Should().Be(Expected);
    }

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("UNABLE TO CONNECT")]
    [InlineData("SEARCHING...\rUNABLE TO CONNECT\r\r>")]
    [InlineData("?")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">")]
    [InlineData("014\r0:490201575657\r1:5A5A5A314A5A58\r\r>")]               // son parça eksik (10 karakter)
    [InlineData("014\r0:490201575657\r1:5A5A5A314A5A58\r2:5730303030\r\r>")] // yarım
    [InlineData("014\r0:490201495657\r1:5A5A5A314A5A58\r2:57303030303031\r\r>")] // 'I' harfi var
    [InlineData("014\r0:490201575657\r1:5A5A5A314A5A58\r2:5730303030ZZ\r\r>")]   // bozuk hex
    [InlineData("014\r0:410C01575657\r1:5A5A5A314A5A58\r2:57303030303031\r\r>")] // başka cevap
    [InlineData("41 0C 1A F8")]
    [InlineData("7F 09 11")]
    public void Bad_Input_Returns_False(string raw)
    {
        VinParser.TryParse(raw, out var vin).Should().BeFalse();
        vin.Should().BeEmpty();
    }

    [Fact]
    public void Null_Input_Returns_False()
    {
        VinParser.TryParse(null, out _).Should().BeFalse();
    }
}
