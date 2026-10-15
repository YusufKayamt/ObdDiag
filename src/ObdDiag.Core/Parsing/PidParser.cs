namespace ObdDiag.Core.Parsing;

/// <summary>
/// Canlı veri cevaplarını (Mod 01) anlamlı değere çevirir.
/// Her metot: başarılıysa true + değer, cevap bozuk/boş/"NO DATA" ise false döner.
/// Formüllerde A ilk, B ikinci veri baytıdır.
/// </summary>
public static class PidParser
{
    private const byte PidCoolantTemp = 0x05;
    private const byte PidRpm = 0x0C;
    private const byte PidSpeed = 0x0D;
    private const byte PidThrottle = 0x11;

    /// <summary>Motor devri (rpm) = (256*A + B) / 4. Örn. "41 0C 1A F8" -> 1726.</summary>
    public static bool TryParseRpm(string? response, out double rpm)
    {
        rpm = 0;
        if (!ElmResponse.TryGetPidData(response, PidRpm, out var d) || d.Length < 2)
            return false;

        // A'yı 8 bit sola kaydırıp B ile birleştiriyoruz: 256*A + B
        var raw = (d[0] << 8) | d[1];
        rpm = raw / 4.0;
        return true;
    }

    /// <summary>Araç hızı (km/s) = A.</summary>
    public static bool TryParseSpeed(string? response, out int speed)
    {
        speed = 0;
        if (!ElmResponse.TryGetPidData(response, PidSpeed, out var d) || d.Length < 1)
            return false;

        speed = d[0];
        return true;
    }

    /// <summary>Soğutma suyu sıcaklığı (°C) = A - 40. Örn. "41 05 7B" -> 83.</summary>
    public static bool TryParseCoolantTemp(string? response, out int celsius)
    {
        celsius = 0;
        if (!ElmResponse.TryGetPidData(response, PidCoolantTemp, out var d) || d.Length < 1)
            return false;

        celsius = d[0] - 40;
        return true;
    }

    /// <summary>Gaz kelebeği konumu (%) = A * 100 / 255.</summary>
    public static bool TryParseThrottle(string? response, out double percent)
    {
        percent = 0;
        if (!ElmResponse.TryGetPidData(response, PidThrottle, out var d) || d.Length < 1)
            return false;

        percent = d[0] * 100.0 / 255.0;
        return true;
    }
}
