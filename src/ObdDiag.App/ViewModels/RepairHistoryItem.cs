using System.Globalization;
using ObdDiag.Data.Entities;

namespace ObdDiag.App.ViewModels;

/// <summary>Tamir geçmişi listesindeki bir satırın ekranda görünen hali.</summary>
/// <param name="Record">Satırın ait olduğu kayıt (PDF rapor bu kayıttan üretilir).</param>
public sealed record RepairHistoryItem(string DateText, string Description, string? MileageText, string? CodesText, string? SnapshotText, RepairRecord Record)
{
    public static RepairHistoryItem From(RepairRecord record)
    {
        var codes = record.DtcRecords.Count == 0
            ? null
            : "Arıza kodları: " + string.Join(", ", record.DtcRecords.Select(d => d.WasCleared ? $"{d.Code} (silindi)" : d.Code));

        string? snapshot = null;
        var s = record.LiveDataSnapshots.FirstOrDefault();
        if (s is not null)
        {
            var parts = new List<string>();
            if (s.Rpm is { } rpm) parts.Add($"{rpm:0} devir/dk");
            if (s.Speed is { } speed) parts.Add($"{speed} km/s");
            if (s.CoolantTemp is { } temp) parts.Add($"{temp} °C");
            if (s.Throttle is { } thr) parts.Add($"gaz %{thr:0}");
            if (parts.Count > 0)
                snapshot = "Canlı veri: " + string.Join(" · ", parts);
        }

        return new RepairHistoryItem(
            record.Date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            record.Description,
            record.Mileage is { } km ? $"{km:N0} km".Replace(',', '.') : null,
            codes,
            snapshot,
            record);
    }
}
