namespace ObdDiag.Core.Parsing;

/// <summary>
/// Şasi numarası (VIN) kontrolü. Kurallar: tam 17 karakter, sadece harf ve rakam,
/// I, O ve Q harfleri kullanılmaz (1 ve 0 ile karışmasın diye).
/// </summary>
public static class VinValidator
{
    public const int Length = 17;

    /// <summary>Baştaki/sondaki boşlukları ve aradaki boşlukları siler, harfleri büyütür.</summary>
    public static string Normalize(string? vin) =>
        (vin ?? string.Empty).Replace(" ", "").Trim().ToUpperInvariant();

    public static bool IsValid(string? vin) => GetError(vin) is null;

    /// <summary>VIN geçerliyse null, değilse kullanıcıya gösterilecek Türkçe hata mesajı döner.</summary>
    public static string? GetError(string? vin)
    {
        var v = Normalize(vin);

        if (v.Length == 0)
            return "Şasi numarası (VIN) boş olamaz.";
        if (v.Length != Length)
            return $"Şasi numarası (VIN) tam {Length} karakter olmalı (şu an {v.Length} karakter).";
        if (!v.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            return "Şasi numarası (VIN) sadece harf ve rakamlardan oluşabilir.";
        if (v.IndexOfAny(new[] { 'I', 'O', 'Q' }) >= 0)
            return "Şasi numarasında (VIN) I, O ve Q harfleri bulunmaz. (I yerine 1, O ve Q yerine 0 olabilir.)";

        return null;
    }
}
