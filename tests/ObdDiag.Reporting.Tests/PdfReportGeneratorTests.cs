using System.Text;
using FluentAssertions;
using ObdDiag.Core.Models;
using ObdDiag.Reporting;

namespace ObdDiag.Reporting.Tests;

public class PdfReportGeneratorTests
{
    /// <summary>1x1 piksellik geçerli bir PNG resmi (logo yerine).</summary>
    internal static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly PdfReportGenerator _generator = new();

    private static DiagnosticReport FullReport(byte[]? logo = null) => new()
    {
        Workshop = new WorkshopInfo("Kaya Oto Elektrik", "Sanayi Sitesi 3. Blok No:12, Elazığ", "0424 123 45 67", "info@kayaoto.com", logo),
        ReportNumber = "000042",
        Date = new DateTime(2026, 10, 21, 14, 30, 0),
        CustomerName = "Ahmet Yılmaz",
        CustomerPhone = "0532 111 22 33",
        Vin = "WVWZZZ1JZXW000001",
        Plate = "23 ABC 456",
        Make = "Volkswagen",
        Model = "Golf",
        Year = 2015,
        Mileage = 154_300,
        Faults = new[]
        {
            new ReportFault("P0104", "Hava akış sensörü (MAF) devresi aralıklı arıza", WasCleared: true),
            new ReportFault("P0301", null, WasCleared: false)
        },
        LiveData = new LiveData(812, 0, 88, 2),
        WorkPerformed = "Hava akış sensörü soketi temizlendi, arıza kodları silindi. Şişirme hortumu kontrol edildi."
    };

    private static void ShouldBePdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Full_Report_Is_Generated_As_Pdf()
    {
        ShouldBePdf(_generator.GeneratePdf(FullReport()));
    }

    [Fact]
    public void Report_With_Logo_Is_Generated()
    {
        var withLogo = _generator.GeneratePdf(FullReport(TinyPng));
        ShouldBePdf(withLogo);
    }

    [Fact]
    public void Broken_Logo_Is_Skipped_Instead_Of_Failing()
    {
        var broken = Encoding.ASCII.GetBytes("bu bir resim değil");
        ShouldBePdf(_generator.GeneratePdf(FullReport(broken)));
    }

    [Fact]
    public void Minimal_Report_Without_Optional_Fields_Is_Generated()
    {
        var report = new DiagnosticReport
        {
            Workshop = new WorkshopInfo(""),
            CustomerName = "Müşteri",
            Vin = "WVWZZZ1JZXW000001"
        };

        ShouldBePdf(_generator.GeneratePdf(report));
    }

    [Fact]
    public void Live_Data_With_Missing_Values_Is_Generated()
    {
        var report = new DiagnosticReport
        {
            Workshop = new WorkshopInfo("Servis"),
            CustomerName = "Müşteri",
            Vin = "WVWZZZ1JZXW000001",
            LiveData = new LiveData(null, 0, null, null)
        };

        ShouldBePdf(_generator.GeneratePdf(report));
    }

    [Fact]
    public void Many_Fault_Codes_Flow_To_Next_Page()
    {
        var faults = Enumerable.Range(0, 80)
            .Select(i => new ReportFault($"P{i:0000}", "Uzun bir açıklama satırı, sayfa taşmasını denemek için", i % 2 == 0))
            .ToArray();
        var report = new DiagnosticReport
        {
            Workshop = new WorkshopInfo("Servis"),
            CustomerName = "Müşteri",
            Vin = "WVWZZZ1JZXW000001",
            Faults = faults
        };

        var single = _generator.GeneratePdf(FullReport());
        var multi = _generator.GeneratePdf(report);

        ShouldBePdf(multi);
        CountPages(multi).Should().BeGreaterThan(1);
        CountPages(single).Should().Be(1);
    }

    [Fact]
    public void Null_Report_Throws()
    {
        var act = () => _generator.GeneratePdf(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Resim kontrolü ----

    [Fact]
    public void Png_Is_Supported_Image()
    {
        _generator.IsSupportedImage(TinyPng).Should().BeTrue();
    }

    [Fact]
    public void Random_Bytes_And_Empty_Data_Are_Not_Images()
    {
        _generator.IsSupportedImage(Encoding.ASCII.GetBytes("merhaba")).Should().BeFalse();
        _generator.IsSupportedImage(Array.Empty<byte>()).Should().BeFalse();
        _generator.IsSupportedImage(null!).Should().BeFalse();
    }

    // ---- Arıza açıklaması ----

    [Fact]
    public void Known_Description_Is_Used_As_Is()
    {
        PdfReportGenerator.DescribeFault(new ReportFault("P0104", "MAF sensörü arızası", false))
            .Should().Be("MAF sensörü arızası");
    }

    [Theory]
    [InlineData("P0301", "Motor / şanzıman")]
    [InlineData("C0035", "Şasi")]
    [InlineData("B1000", "Gövde")]
    [InlineData("U0100", "iletişim ağı")]
    public void Missing_Description_Falls_Back_To_System_Name(string code, string expected)
    {
        PdfReportGenerator.DescribeFault(new ReportFault(code, null, false))
            .Should().Contain(expected);
    }

    [Fact]
    public void Unknown_Code_Without_Description_Says_Not_Found()
    {
        PdfReportGenerator.DescribeFault(new ReportFault("X123", null, false))
            .Should().Be("Açıklama bulunamadı.");
    }

    /// <summary>PDF içindeki "/Type /Page" nesnelerini sayar (sayfa ağacı "/Type /Pages" hariç).</summary>
    private static int CountPages(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?![s\w])").Count;
    }
}
