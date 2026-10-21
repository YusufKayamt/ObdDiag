namespace ObdDiag.Reporting;

/// <summary>Arıza raporunu dosyaya dönüştüren servis.</summary>
public interface IReportGenerator
{
    /// <summary>Raporu PDF olarak üretir ve dosyanın baytlarını döner.</summary>
    byte[] GeneratePdf(DiagnosticReport report);

    /// <summary>Verilen baytlar raporda kullanılabilecek bir resim mi (PNG, JPG vb.)?</summary>
    bool IsSupportedImage(byte[] data);
}
