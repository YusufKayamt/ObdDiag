namespace ObdDiag.Core.Interfaces;

/// <summary>Bilgisayardaki COM portlarını listeler (arayüz seri porta doğrudan dokunmasın diye).</summary>
public interface IComPortProvider
{
    /// <summary>Bulunan port adları, örn. "COM3". Sıralıdır (COM3, COM4, COM10...).</summary>
    IReadOnlyList<string> GetPortNames();
}
