using FluentAssertions;
using ObdDiag.Core.Parsing;

namespace ObdDiag.Core.Tests;

public class DtcParserTests
{
    // ---- Klasik biçim: 43 + 3 çift (boşlar 00 00 ile doldurulur) ----

    [Fact]
    public void Classic_Single_Code()
    {
        DtcParser.TryParse("43 01 04 00 00 00 00", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104");
    }

    [Fact]
    public void Classic_Three_Codes()
    {
        DtcParser.TryParse("43 01 04 03 01 01 33", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104", "P0301", "P0133");
    }

    [Fact]
    public void Classic_No_Codes_Returns_Empty_List()
    {
        DtcParser.TryParse("43 00 00 00 00 00 00", out var codes).Should().BeTrue();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void Classic_Across_Multiple_Lines()
    {
        DtcParser.TryParse("43 01 04 03 01 00 00\r43 01 33 00 00 00 00\r\r>", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104", "P0301", "P0133");
    }

    // ---- CAN biçimi: 43 + kod sayısı + kodlar ----

    [Fact]
    public void Can_Two_Codes()
    {
        DtcParser.TryParse("43 02 01 04 03 01", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104", "P0301");
    }

    [Fact]
    public void Can_One_Code()
    {
        DtcParser.TryParse("43 01 01 04", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104");
    }

    [Fact]
    public void Can_No_Codes()
    {
        DtcParser.TryParse("43 00", out var codes).Should().BeTrue();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void Can_Count_Larger_Than_Data_Is_Rejected()
    {
        // 3 kod diyor ama sadece 1 kod var -> bozuk
        DtcParser.TryParse("43 03 01 04", out var codes).Should().BeFalse();
        codes.Should().BeEmpty();
    }

    // ---- Biçim farkları ----

    [Theory]
    [InlineData("430201040301")]
    [InlineData("43 02 01 04 03 01\r\r>")]
    [InlineData("03\r43020104 0301\r\r>")]          // yankı açıkken
    [InlineData("SEARCHING...\r43 02 01 04 03 01\r\r>")]
    [InlineData("43 02 01 04 03 01 \r\r>")]
    public void Different_Formatting_Gives_Same_Result(string response)
    {
        DtcParser.TryParse(response, out var codes).Should().BeTrue();
        codes.Should().Equal("P0104", "P0301");
    }

    [Fact]
    public void Lowercase_Is_Accepted()
    {
        DtcParser.TryParse("43 01 3f 00 00 00 00", out var codes).Should().BeTrue();
        codes.Should().Equal("P013F");
    }

    // ---- Harf ve rakam çözümleme (bit işlemleri) ----

    [Theory]
    [InlineData("43 01 04 00 00 00 00", "P0104")]   // 00 -> P
    [InlineData("43 41 23 00 00 00 00", "C0123")]   // 01 -> C
    [InlineData("43 81 23 00 00 00 00", "B0123")]   // 10 -> B
    [InlineData("43 C1 23 00 00 00 00", "U0123")]   // 11 -> U
    [InlineData("43 93 45 00 00 00 00", "B1345")]   // 1. rakam 1
    [InlineData("43 23 45 00 00 00 00", "P2345")]   // 1. rakam 2
    [InlineData("43 33 45 00 00 00 00", "P3345")]   // 1. rakam 3
    [InlineData("43 FF FF 00 00 00 00", "U3FFF")]   // en büyük değer
    public void Letter_And_Digits_Are_Decoded_From_Bits(string response, string expected)
    {
        DtcParser.TryParse(response, out var codes).Should().BeTrue();
        codes.Should().Equal(expected);
    }

    // ---- Hatalı / bozuk cevaplar ----

    [Theory]
    [InlineData("NO DATA\r\r>")]
    [InlineData("UNABLE TO CONNECT")]
    [InlineData("SEARCHING...\rUNABLE TO CONNECT\r\r>")]
    [InlineData("?")]
    [InlineData("STOPPED")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">")]
    [InlineData("43")]                       // hiç veri yok
    [InlineData("43 01 0")]                  // yarım bayt
    [InlineData("43 01 ZZ 00 00 00 00")]     // hex olmayan karakter
    [InlineData("44")]                       // silme cevabı, kod cevabı değil
    [InlineData("41 0C 1A F8")]              // başka mod
    [InlineData("7F 03 11")]                 // olumsuz cevap
    public void Bad_Input_Returns_False(string response)
    {
        DtcParser.TryParse(response, out var codes).Should().BeFalse();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void Null_Input_Returns_False()
    {
        DtcParser.TryParse(null!, out var codes).Should().BeFalse();
        codes.Should().BeEmpty();
    }

    [Fact]
    public void Same_Code_From_Two_Ecus_Is_Listed_Once()
    {
        DtcParser.TryParse("43 01 04 00 00 00 00\r43 01 04 00 00 00 00\r\r>", out var codes).Should().BeTrue();
        codes.Should().Equal("P0104");
    }

    [Fact]
    public void ClearConfirmation_Is_Recognized()
    {
        DtcParser.IsClearConfirmed("44\r\r>").Should().BeTrue();
        DtcParser.IsClearConfirmed("04\r44\r\r>").Should().BeTrue();
        DtcParser.IsClearConfirmed("NO DATA").Should().BeFalse();
        DtcParser.IsClearConfirmed("43 00").Should().BeFalse();
        DtcParser.IsClearConfirmed("").Should().BeFalse();
    }
}
