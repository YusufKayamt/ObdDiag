namespace ObdDiag.Data;

/// <summary>Kayıt kurallarına uymayan işlem (örn. geçersiz VIN, aynı VIN'in tekrarı). Mesajı sade Türkçedir.</summary>
public sealed class DataValidationException : Exception
{
    public DataValidationException(string message) : base(message) { }
}
