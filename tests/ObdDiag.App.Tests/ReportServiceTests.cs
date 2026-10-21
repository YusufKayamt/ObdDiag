using System.IO;
using FluentAssertions;
using Moq;
using ObdDiag.App.Services;
using ObdDiag.Data.Entities;
using ObdDiag.Reporting;

namespace ObdDiag.App.Tests;

public class ReportServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ObdDiagTests_" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IReportGenerator> _generator = new();
    private readonly Mock<ISettingsStore> _settings = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private DiagnosticReport? _generated;

    private static readonly Customer Customer = new() { Id = 1, FullName = "Ahmet Yılmaz", Phone = "0532 111 22 33" };
    private static readonly Vehicle Vehicle = new()
    {
        Id = 2, CustomerId = 1, Vin = "WVWZZZ1JZXW000001", Plate = "23 ABC 456", Make = "Volkswagen", Model = "Golf", Year = 2015
    };

    private static RepairRecord Record() => new()
    {
        Id = 42,
        VehicleId = 2,
        Date = new DateTime(2026, 10, 21, 14, 30, 0),
        Mileage = 154_300,
        Description = "MAF sensörü temizlendi",
        DtcRecords =
        {
            new DtcRecord { Code = "P0104", Description = "Hava akış sensörü", WasCleared = true },
            new DtcRecord { Code = "P0301", WasCleared = false }
        },
        LiveDataSnapshots =
        {
            new LiveDataSnapshot { CapturedAt = new DateTime(2026, 10, 21, 14, 0, 0), Rpm = 3000, Speed = 50 },
            new LiveDataSnapshot { CapturedAt = new DateTime(2026, 10, 21, 14, 29, 0), Rpm = 810, Speed = 0, CoolantTemp = 88, Throttle = 2 }
        }
    };

    public ReportServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _settings.Setup(s => s.Load()).Returns(new WorkshopSettings { Name = "Kaya Oto", Phone = "0424 123 45 67" });
        _generator.Setup(g => g.GeneratePdf(It.IsAny<DiagnosticReport>()))
            .Callback<DiagnosticReport>(r => _generated = r)
            .Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // "%PDF"
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ReportService Create() => new(_generator.Object, _settings.Object, _dialogs.Object);

    [Fact]
    public void Report_Is_Built_From_Records()
    {
        var report = ReportService.BuildReport(new WorkshopSettings { Name = "Kaya Oto" }, Customer, Vehicle, Record());

        report.Workshop.Name.Should().Be("Kaya Oto");
        report.ReportNumber.Should().Be("000042");
        report.CustomerName.Should().Be("Ahmet Yılmaz");
        report.CustomerPhone.Should().Be("0532 111 22 33");
        report.Plate.Should().Be("23 ABC 456");
        report.Vin.Should().Be("WVWZZZ1JZXW000001");
        report.Year.Should().Be(2015);
        report.Mileage.Should().Be(154_300);
        report.WorkPerformed.Should().Be("MAF sensörü temizlendi");
        report.Faults.Should().Equal(
            new ReportFault("P0104", "Hava akış sensörü", true),
            new ReportFault("P0301", null, false));
    }

    [Fact]
    public void Latest_Live_Snapshot_Is_Used()
    {
        var report = ReportService.BuildReport(new WorkshopSettings(), Customer, Vehicle, Record());

        report.LiveData.Should().NotBeNull();
        report.LiveData!.Rpm.Should().Be(810);
        report.LiveData.Speed.Should().Be(0);
        report.LiveData.CoolantTemp.Should().Be(88);
    }

    [Fact]
    public void Record_Without_Snapshot_Has_No_Live_Data()
    {
        var record = new RepairRecord { Id = 1, Description = "Yağ değişimi", Date = DateTime.Now };

        var report = ReportService.BuildReport(new WorkshopSettings(), Customer, Vehicle, record);

        report.LiveData.Should().BeNull();
        report.Faults.Should().BeEmpty();
    }

    [Fact]
    public void Default_File_Name_Uses_Plate_And_Date()
    {
        ReportService.DefaultFileName(Vehicle, Record()).Should().Be("Rapor_23ABC456_20261021_1430.pdf");
    }

    [Fact]
    public void Default_File_Name_Uses_Vin_When_No_Plate_And_Removes_Bad_Characters()
    {
        var noPlate = new Vehicle { Vin = "WVWZZZ1JZXW000001" };
        var slashPlate = new Vehicle { Vin = "WVWZZZ1JZXW000001", Plate = "34/AB:12" };

        ReportService.DefaultFileName(noPlate, Record()).Should().Be("Rapor_WVWZZZ1JZXW000001_20261021_1430.pdf");
        ReportService.DefaultFileName(slashPlate, Record()).Should().Be("Rapor_34AB12_20261021_1430.pdf");
    }

    [Fact]
    public async Task Export_Writes_File_With_Saved_Workshop_And_Opens_It()
    {
        var path = Path.Combine(_folder, "rapor.pdf");
        _dialogs.Setup(d => d.AskSaveFile(It.IsAny<string>(), "Rapor_23ABC456_20261021_1430.pdf", It.IsAny<string>()))
            .Returns(path);

        var result = await Create().ExportAsync(Customer, Vehicle, Record());

        result.Should().Be(path);
        File.ReadAllBytes(path).Should().Equal(0x25, 0x50, 0x44, 0x46);
        _generated!.Workshop.Name.Should().Be("Kaya Oto");
        _dialogs.Verify(d => d.OpenFile(path), Times.Once);
    }

    [Fact]
    public async Task Cancelled_Save_Dialog_Creates_Nothing()
    {
        var result = await Create().ExportAsync(Customer, Vehicle, Record()); // AskSaveFile null döner

        result.Should().BeNull();
        _generator.Verify(g => g.GeneratePdf(It.IsAny<DiagnosticReport>()), Times.Never);
        _dialogs.Verify(d => d.OpenFile(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Errors_Are_Explained_In_Plain_Turkish()
    {
        ReportService.DescribeError(new IOException()).Should().Contain("başka bir programda açık");
        ReportService.DescribeError(new UnauthorizedAccessException()).Should().Contain("yazma izni yok");
    }
}
