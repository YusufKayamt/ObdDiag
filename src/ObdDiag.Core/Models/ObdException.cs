namespace ObdDiag.Core.Models;

/// <summary>
/// Araçla veya cihazla konuşurken oluşan, kullanıcıya gösterilebilecek hata.
/// Mesajı sade Türkçe yazılır.
/// </summary>
public sealed class ObdException : Exception
{
    public ObdException(string message) : base(message) { }
    public ObdException(string message, Exception inner) : base(message, inner) { }
}
