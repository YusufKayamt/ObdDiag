namespace ObdDiag.Core.Parsing;

/// <summary>
/// ELM327'den gelen ham metni temizleyen yardımcı sınıf.
/// Ham cevap örneği: "SEARCHING...\r41 0C 1A F8\r\r&gt;"
/// </summary>
public static class ElmResponse
{
    /// <summary>
    /// Cevabı satırlara böler. Her satırdan boşluklar ve '&gt;' atılır, harfler büyütülür.
    /// "SEARCHING..." ve "BUS INIT: ...OK" gibi bilgi satırları çıkarılır.
    /// </summary>
    public static IReadOnlyList<string> GetLines(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var part in raw.Split('\r', '\n'))
        {
            var line = part.Replace(" ", "").Replace(">", "").Trim().ToUpperInvariant();
            if (line.Length == 0 || line == "SEARCHING..." || line.StartsWith("BUSINIT", StringComparison.Ordinal))
                continue;
            result.Add(line);
        }
        return result;
    }

    /// <summary>Satır geçerli bir hex metin mi? (boş değil, çift sayıda karakter, hepsi 0-9/A-F)</summary>
    public static bool TryParseHexLine(string line, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (line.Length == 0 || line.Length % 2 != 0)
            return false;

        var result = new byte[line.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            if (!Uri.IsHexDigit(line[2 * i]) || !Uri.IsHexDigit(line[2 * i + 1]))
                return false;
            result[i] = Convert.ToByte(line.Substring(2 * i, 2), 16);
        }

        bytes = result;
        return true;
    }

    /// <summary>
    /// Mod 01 cevabından veri baytlarını çıkarır. Örn. mode=0x01, pid=0x0C ve cevap "41 0C 1A F8"
    /// ise veri = { 0x1A, 0xF8 }. Cevap başlığı (mod + 0x40, PID) uymazsa veya satır bozuksa false döner.
    /// </summary>
    public static bool TryGetPidData(string? raw, byte pid, out byte[] data)
    {
        data = Array.Empty<byte>();
        const byte answerMode = 0x01 + 0x40; // cevap, istek modu + 0x40 ile başlar (01 -> 41)

        foreach (var line in GetLines(raw))
        {
            if (!TryParseHexLine(line, out var bytes))
                continue;
            if (bytes.Length < 2 || bytes[0] != answerMode || bytes[1] != pid)
                continue;

            data = bytes[2..];
            return true;
        }
        return false;
    }
}
