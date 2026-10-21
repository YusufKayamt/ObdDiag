using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ObdDiag.Reporting;

/// <summary>
/// Arıza raporunu QuestPDF ile A4 PDF'e çevirir.
/// Sayfa düzeni: başlık (logo + işletme), müşteri/araç bilgileri, arıza kodları, canlı veri özeti,
/// yapılan işlem ve imza alanı.
/// </summary>
public sealed class PdfReportGenerator : IReportGenerator
{
    private const string PrimaryColor = "#1F4E79";
    private const string MutedColor = "#6B7A8A";
    private const string BorderColor = "#D9E0E8";
    private const string HeaderFill = "#E8EEF5";
    private const string DangerColor = "#C62828";
    private const string OkColor = "#2E7D32";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    static PdfReportGenerator()
    {
        // QuestPDF, yıllık geliri belli bir sınırın altındaki işletmeler ve öğrenciler için ücretsizdir (Community lisansı).
        QuestPDF.Settings.License = LicenseType.Community;
        // Türkçe harfleri (ğ, ş, ı, İ...) içeren Windows yazı tiplerini kullanabilmek için.
        QuestPDF.Settings.UseSystemFonts = true;
    }

    public byte[] GeneratePdf(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontFamily("Arial", "Segoe UI").FontSize(10).FontColor("#1E2A36"));

                page.Header().Element(c => ComposeHeader(c, report));
                page.Content().PaddingTop(14).Element(c => ComposeContent(c, report));
                page.Footer().Element(ComposeFooter);
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = "Arıza Tespit Raporu",
            Author = report.Workshop.Name,
            Creator = "ObdDiag"
        })
        .GeneratePdf();
    }

    public bool IsSupportedImage(byte[] data)
    {
        if (data is null || data.Length == 0)
            return false;
        try
        {
            Image.FromBinaryData(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ================= Başlık =================

    private void ComposeHeader(IContainer container, DiagnosticReport report)
    {
        var shop = report.Workshop;
        var hasLogo = shop.Logo is not null && IsSupportedImage(shop.Logo);

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (hasLogo)
                    row.ConstantItem(90).Height(60).AlignMiddle().Image(shop.Logo!).FitArea();

                row.RelativeItem().PaddingLeft(hasLogo ? 12 : 0).Column(info =>
                {
                    info.Item().Text(string.IsNullOrWhiteSpace(shop.Name) ? "İşletme adı girilmemiş" : shop.Name)
                        .FontSize(16).Bold().FontColor(PrimaryColor);
                    if (!string.IsNullOrWhiteSpace(shop.Address))
                        info.Item().Text(shop.Address).FontColor(MutedColor);

                    var contact = string.Join("   ", new[] { Prefix("Tel: ", shop.Phone), Prefix("E-posta: ", shop.Email) }
                        .Where(s => s is not null));
                    if (contact.Length > 0)
                        info.Item().Text(contact).FontColor(MutedColor);
                });

                row.ConstantItem(150).AlignRight().Column(meta =>
                {
                    meta.Item().AlignRight().Text("ARIZA TESPİT RAPORU").FontSize(12).Bold().FontColor(PrimaryColor);
                    if (!string.IsNullOrWhiteSpace(report.ReportNumber))
                        meta.Item().AlignRight().Text($"Rapor No: {report.ReportNumber}");
                    meta.Item().AlignRight().Text($"Tarih: {report.Date.ToString("dd.MM.yyyy HH:mm", Turkish)}");
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(2).LineColor(PrimaryColor);
        });
    }

    // ================= Gövde =================

    private void ComposeContent(IContainer container, DiagnosticReport report)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            // Müşteri ve araç yan yana
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => InfoBox(c, "MÜŞTERİ BİLGİLERİ", new[]
                {
                    ("Ad Soyad", report.CustomerName),
                    ("Telefon", report.CustomerPhone)
                }));
                row.ConstantItem(12);
                row.RelativeItem().Element(c => InfoBox(c, "ARAÇ BİLGİLERİ", new[]
                {
                    ("Plaka", report.Plate),
                    ("Marka / Model", JoinNonEmpty(" ", report.Make, report.Model)),
                    ("Model Yılı", report.Year?.ToString(Turkish)),
                    ("Şasi No (VIN)", report.Vin),
                    ("Kilometre", report.Mileage is { } km ? $"{km.ToString("N0", Turkish)} km" : null)
                }));
            });

            column.Item().Element(c => ComposeFaults(c, report));
            column.Item().Element(c => ComposeLiveData(c, report));

            column.Item().Column(work =>
            {
                work.Item().Element(SectionTitle).Text("YAPILAN İŞLEM");
                work.Item().Border(1).BorderColor(BorderColor).Padding(8).MinHeight(50)
                    .Text(string.IsNullOrWhiteSpace(report.WorkPerformed) ? "—" : report.WorkPerformed);
            });

            column.Item().PaddingTop(10).Element(ComposeSignatures);
        });
    }

    private static void ComposeFaults(IContainer container, DiagnosticReport report)
    {
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("ARIZA KODLARI");

            if (report.Faults.Count == 0)
            {
                column.Item().Border(1).BorderColor(BorderColor).Padding(8)
                    .Text("Araçta kayıtlı arıza kodu bulunamadı.").FontColor(OkColor).SemiBold();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(70);
                    columns.RelativeColumn();
                    columns.ConstantColumn(85);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Kod");
                    header.Cell().Element(HeaderCell).Text("Açıklama");
                    header.Cell().Element(HeaderCell).Text("Durum");
                });

                foreach (var fault in report.Faults)
                {
                    table.Cell().Element(BodyCell).Text(fault.Code).Bold().FontColor(DangerColor);
                    table.Cell().Element(BodyCell).Text(DescribeFault(fault));
                    table.Cell().Element(BodyCell).Text(fault.WasCleared ? "Silindi" : "Silinmedi")
                        .FontColor(fault.WasCleared ? OkColor : MutedColor);
                }
            });
        });
    }

    private static void ComposeLiveData(IContainer container, DiagnosticReport report)
    {
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("CANLI VERİ ÖZETİ (teşhis anında)");

            var data = report.LiveData;
            if (data is null)
            {
                column.Item().Border(1).BorderColor(BorderColor).Padding(8)
                    .Text("Bu işlemde canlı veri alınmadı.").FontColor(MutedColor);
                return;
            }

            var values = new[]
            {
                ("Motor Devri", data.Rpm is { } rpm ? $"{rpm.ToString("0", Turkish)} devir/dk" : null),
                ("Araç Hızı", data.Speed is { } speed ? $"{speed} km/s" : null),
                ("Motor Sıcaklığı", data.CoolantTemp is { } temp ? $"{temp} °C" : null),
                ("Gaz Pedalı", data.Throttle is { } throttle ? $"% {throttle.ToString("0", Turkish)}" : null)
            };

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in values)
                        columns.RelativeColumn();
                });

                foreach (var (label, _) in values)
                    table.Cell().Element(HeaderCell).Text(label);
                foreach (var (_, value) in values)
                    table.Cell().Element(BodyCell).Text(value ?? "Okunamadı").SemiBold();
            });
        });
    }

    private static void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => SignatureBox(c, "Teknisyen"));
            row.ConstantItem(40);
            row.RelativeItem().Element(c => SignatureBox(c, "Müşteri"));
        });
    }

    private static void SignatureBox(IContainer container, string title)
    {
        container.Column(column =>
        {
            column.Item().Text(title).Bold();
            column.Item().PaddingTop(4).Text("Ad Soyad:").FontColor(MutedColor);
            column.Item().PaddingTop(36).LineHorizontal(1).LineColor(MutedColor);
            column.Item().AlignCenter().Text("İmza").FontSize(9).FontColor(MutedColor);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(BorderColor);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Bu rapor ObdDiag ile oluşturulmuştur.").FontSize(8).FontColor(MutedColor);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(MutedColor));
                    text.Span("Sayfa ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }

    // ================= Yardımcılar =================

    private static void InfoBox(IContainer container, string title, IEnumerable<(string Label, string? Value)> rows)
    {
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text(title);
            column.Item().Border(1).BorderColor(BorderColor).Padding(8).Column(lines =>
            {
                lines.Spacing(3);
                foreach (var (label, value) in rows)
                {
                    lines.Item().Row(row =>
                    {
                        row.ConstantItem(90).Text(label).FontColor(MutedColor);
                        row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).SemiBold();
                    });
                }
            });
        });
    }

    private static IContainer SectionTitle(IContainer container) =>
        container.PaddingBottom(4).DefaultTextStyle(s => s.FontSize(11).Bold().FontColor(PrimaryColor));

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(HeaderFill).Border(1).BorderColor(BorderColor).Padding(5)
            .DefaultTextStyle(s => s.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.Border(1).BorderColor(BorderColor).Padding(5);

    /// <summary>
    /// Kodun açıklaması yoksa en azından hangi sisteme ait olduğunu yazar.
    /// İlk harf sistemi gösterir: P = motor/şanzıman, C = şasi, B = gövde, U = iletişim ağı.
    /// </summary>
    public static string DescribeFault(ReportFault fault)
    {
        if (!string.IsNullOrWhiteSpace(fault.Description))
            return fault.Description;

        var system = fault.Code.Length > 0 ? char.ToUpperInvariant(fault.Code[0]) switch
        {
            'P' => "Motor / şanzıman",
            'C' => "Şasi (fren, direksiyon, süspansiyon)",
            'B' => "Gövde (klima, hava yastığı, kapılar vb.)",
            'U' => "Araç içi iletişim ağı",
            _ => null
        } : null;

        return system is null ? "Açıklama bulunamadı." : $"{system} sistemine ait arıza (ayrıntılı açıklama bulunamadı).";
    }

    private static string? Prefix(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : prefix + value.Trim();

    private static string? JoinNonEmpty(string separator, params string?[] parts)
    {
        var text = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }
}
