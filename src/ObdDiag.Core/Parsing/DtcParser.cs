namespace ObdDiag.Core.Parsing;

/// <summary>
/// Arıza kodu (DTC) cevaplarını çözer. İki biçimi de destekler:
/// <list type="bullet">
/// <item>Klasik: "43 01 04 03 01 00 00" -> "43"ten sonra çift çift kodlar (boşluk doldurma 00 00).</item>
/// <item>CAN: "43 02 01 04 03 01" -> "43"ten sonra önce kod sayısı, sonra kodlar.</item>
/// </list>
/// Ayrım: "43"ten sonraki bayt sayısı TEK ise CAN, ÇİFT ise klasik kabul edilir.
/// (Doldurma baytlı CAN cevapları çift gelirse bu ayrım yanılabilir; gerçek araçta denenmeli.)
/// </summary>
public static class DtcParser
{
    private const byte ReadAnswer = 0x43;  // Mod 03 cevabı
    private const byte ClearAnswer = 0x44; // Mod 04 cevabı
    private const string Letters = "PCBU";

    /// <summary>
    /// Mod 03 cevabını çözer. Başarılıysa true (arıza yoksa liste boş). "NO DATA", bozuk veya
    /// beklenmeyen cevapta false döner ve liste boş olur.
    /// </summary>
    public static bool TryParse(string? response, out IReadOnlyList<string> codes)
    {
        codes = Array.Empty<string>();
        var result = new List<string>();
        var foundAnswer = false;

        foreach (var line in ElmResponse.GetLines(response))
        {
            if (!line.StartsWith("43", StringComparison.Ordinal))
                continue; // yankı veya hata satırı; cevap satırı değil

            if (!ElmResponse.TryParseHexLine(line, out var bytes) || bytes.Length < 2)
                return false; // "43" ile başlıyor ama bozuk

            if (!TryReadCodes(bytes[1..], result))
                return false;
            foundAnswer = true;
        }

        if (!foundAnswer)
            return false;

        codes = result;
        return true;
    }

    /// <summary>Mod 04 (arıza silme) cevabı "44" mü?</summary>
    public static bool IsClearConfirmed(string? response) =>
        ElmResponse.GetLines(response).Any(l => ElmResponse.TryParseHexLine(l, out var b) && b.Length == 1 && b[0] == ClearAnswer);

    /// <summary>"43"ten sonraki baytlardan kodları okuyup listeye ekler.</summary>
    private static bool TryReadCodes(byte[] data, List<string> result)
    {
        var start = 0;
        var pairCount = data.Length / 2;

        if (data.Length % 2 == 1)
        {
            // CAN biçimi: ilk bayt kod sayısı
            var count = data[0];
            if (data.Length < 1 + 2 * count)
                return false;
            start = 1;
            pairCount = count;
        }

        for (var i = 0; i < pairCount; i++)
        {
            var b1 = data[start + 2 * i];
            var b2 = data[start + 2 * i + 1];
            if (b1 == 0 && b2 == 0)
                continue; // doldurma, kod değil

            var code = Decode(b1, b2);
            if (!result.Contains(code))
                result.Add(code);
        }
        return true;
    }

    /// <summary>
    /// İki baytı kod yazısına çevirir (bit işlemleriyle):
    /// b1'in en üst 2 biti harf, sonraki 2 bit ilk rakam, alt 4 bit ikinci rakam; b2 son iki rakam.
    /// </summary>
    private static string Decode(byte b1, byte b2)
    {
        var letter = Letters[(b1 >> 6) & 0b11];
        var digit1 = (b1 >> 4) & 0b11;
        var digit2 = b1 & 0x0F;
        return $"{letter}{digit1}{digit2:X}{b2:X2}";
    }
}
