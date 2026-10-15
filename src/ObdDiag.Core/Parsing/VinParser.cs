using System.Text;

namespace ObdDiag.Core.Parsing;

/// <summary>
/// Mod 09 PID 02 (şasi no) cevabını çözer. İki biçimi destekler (SAE J1979 / ISO 15765-4):
/// <list type="bullet">
/// <item>CAN (çok parçalı): "014" (bayt sayısı), sonra "0: 49 02 01 57 56 57", "1: ..." satırları.</item>
/// <item>Klasik: her satır "49 02 &lt;sıra&gt;" ile başlar ve 4 veri baytı taşır; boşluklar 00 ile doldurulur.</item>
/// </list>
/// Not: Bu biçimler standarda göre yazıldı, gerçek araçta henüz denenmedi.
/// </summary>
public static class VinParser
{
    private const byte AnswerMode = 0x49; // Mod 09 cevabı (09 + 0x40)
    private const byte Pid = 0x02;

    /// <summary>Başarılıysa true ve büyük harfli, geçerli 17 karakterlik VIN döner.</summary>
    public static bool TryParse(string? response, out string vin)
    {
        vin = string.Empty;

        var indexed = new SortedDictionary<int, byte[]>(); // "0:", "1:" gibi numaralı satırlar (CAN)
        var plain = new List<byte[]>();                     // numarasız satırlar (klasik)

        foreach (var rawLine in ElmResponse.GetLines(response))
        {
            var text = rawLine;
            int? index = null;
            if (text.Length >= 2 && text[1] == ':' && Uri.IsHexDigit(text[0]))
            {
                index = Convert.ToInt32(text[0].ToString(), 16);
                text = text[2..];
            }

            // Bayt sayısı satırı ("014" gibi, tek sayıda karakter) ve hata satırları burada elenir.
            if (!ElmResponse.TryParseHexLine(text, out var bytes))
                continue;

            if (index is { } i)
                indexed[i] = bytes;
            else
                plain.Add(bytes);
        }

        byte[] data;
        if (indexed.Count > 0)
        {
            var all = indexed.Values.SelectMany(b => b).ToArray();
            if (all.Length < 3 || all[0] != AnswerMode || all[1] != Pid)
                return false;
            data = all[3..]; // 49 02 01 başlığını at
        }
        else
        {
            // Aynı sıra numarasını birden fazla kontrol ünitesi (ECU) göndermiş olabilir: ilkini al.
            var lines = plain
                .Where(b => b.Length >= 3 && b[0] == AnswerMode && b[1] == Pid)
                .GroupBy(b => b[2])
                .Select(g => g.First())
                .OrderBy(b => b[2])
                .ToList();
            if (lines.Count == 0)
                return false;
            data = lines.SelectMany(b => b[3..]).ToArray();
        }

        // 00 baytları doldurmadır, VIN'e dahil değil.
        var chars = data.Where(b => b != 0).ToArray();
        var candidate = Encoding.ASCII.GetString(chars).ToUpperInvariant();
        if (!VinValidator.IsValid(candidate))
            return false;

        vin = candidate;
        return true;
    }
}
