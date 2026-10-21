using System.IO;
using ObdDiag.Core.Models;
using ObdDiag.Data.Entities;
using ObdDiag.Reporting;

namespace ObdDiag.App.Services;

/// <summary>Bir tamir kaydını PDF raporuna çevirip kullanıcının seçtiği yere kaydeder.</summary>
public interface IReportService
{
    /// <summary>
    /// Kullanıcıya kayıt yerini sorar, PDF'i üretip kaydeder ve açar.
    /// Kaydedilen dosyanın yolunu, kullanıcı vazgeçerse null döner.
    /// </summary>
    Task<string?> ExportAsync(Customer customer, Vehicle vehicle, RepairRecord record);
}

public sealed class ReportService : IReportService
{
    private const string PdfFilter = "PDF dosyası (*.pdf)|*.pdf";

    private readonly IReportGenerator _generator;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;

    public ReportService(IReportGenerator generator, ISettingsStore settings, IDialogService dialogs)
    {
        _generator = generator;
        _settings = settings;
        _dialogs = dialogs;
    }

    public async Task<string?> ExportAsync(Customer customer, Vehicle vehicle, RepairRecord record)
    {
        var report = BuildReport(_settings.Load(), customer, vehicle, record);

        var path = _dialogs.AskSaveFile("PDF Raporunu Kaydet", DefaultFileName(vehicle, record), PdfFilter);
        if (path is null)
            return null;

        // PDF üretmek biraz zaman alabilir; ekran donmasın diye arka planda yapılır.
        var bytes = await Task.Run(() => _generator.GeneratePdf(report));
        await File.WriteAllBytesAsync(path, bytes);

        _dialogs.OpenFile(path);
        return path;
    }

    /// <summary>Veritabanı kayıtlarını rapor modeline çevirir.</summary>
    public static DiagnosticReport BuildReport(WorkshopSettings settings, Customer customer, Vehicle vehicle, RepairRecord record)
    {
        var snapshot = record.LiveDataSnapshots.OrderBy(s => s.CapturedAt).LastOrDefault();

        return new DiagnosticReport
        {
            Workshop = new WorkshopInfo(settings.Name, settings.Address, settings.Phone, settings.Email, settings.Logo),
            ReportNumber = record.Id > 0 ? record.Id.ToString("000000") : null,
            Date = record.Date,
            CustomerName = customer.FullName,
            CustomerPhone = customer.Phone,
            Vin = vehicle.Vin,
            Plate = vehicle.Plate,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Year = vehicle.Year,
            Mileage = record.Mileage,
            Faults = record.DtcRecords.Select(d => new ReportFault(d.Code, d.Description, d.WasCleared)).ToList(),
            LiveData = snapshot is null ? null : new LiveData(snapshot.Rpm, snapshot.Speed, snapshot.CoolantTemp, snapshot.Throttle),
            WorkPerformed = record.Description
        };
    }

    /// <summary>PDF kaydederken çıkan hatayı sade Türkçe ile açıklar.</summary>
    public static string DescribeError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Seçilen klasöre yazma izni yok. Başka bir klasör (örn. Belgeler) seçin.",
        IOException => "Dosya yazılamadı. Aynı adlı PDF başka bir programda açıksa kapatıp tekrar deneyin.",
        _ => $"Beklenmeyen bir sorun oluştu: {ex.Message}"
    };

    /// <summary>Örn. "Rapor_23ABC456_20261021_1430.pdf". Plaka yoksa şasi no kullanılır.</summary>
    public static string DefaultFileName(Vehicle vehicle, RepairRecord record)
    {
        var name = string.IsNullOrWhiteSpace(vehicle.Plate) ? vehicle.Vin : vehicle.Plate;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !char.IsWhiteSpace(c) && !invalid.Contains(c)).ToArray());
        return $"Rapor_{safe}_{record.Date:yyyyMMdd_HHmm}.pdf";
    }
}
