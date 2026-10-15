namespace ObdDiag.Core.Interfaces;

/// <summary>
/// ELM327 cihazıyla konuşma kuralı. Gerçek cihaz (seri port) ve sahte cihaz aynı kuralı uygular,
/// böylece uygulamanın geri kalanı hangisiyle konuştuğunu bilmez.
/// </summary>
public interface IElmTransport : IAsyncDisposable
{
    /// <summary>Cihaza şu an bağlı mıyız?</summary>
    bool IsConnected { get; }

    /// <summary>Cihaza bağlanır.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Bağlantıyı kapatır.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Cihaza tek bir komut gönderir (örn. "010C" veya "ATZ") ve cevabı döndürür.
    /// Cevap, gerçek ELM327'deki gibi ham metindir ve sonunda '>' işareti bulunur.
    /// </summary>
    Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default);
}
