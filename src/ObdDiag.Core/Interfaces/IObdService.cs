using ObdDiag.Core.Models;

namespace ObdDiag.Core.Interfaces;

/// <summary>
/// Arayüzün kullanacağı üst düzey servis: "devri oku", "arızaları oku" gibi işleri yapar.
/// Hex ve ELM327 komutları gibi ayrıntıları arayüzden gizler.
/// </summary>
public interface IObdService
{
    /// <summary>Başlatma tamamlandı ve araç cevap veriyor mu?</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Cihaza bağlanır, başlatma komutlarını sırayla gönderir (ATZ, ATE0, ATL0, ATS0, ATH0, ATSP0)
    /// ve aracın cevap verdiğini kontrol eder. Sorun olursa <see cref="ObdException"/> fırlatır.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Devir, hız, sıcaklık ve gaz değerlerini bir kerede okur.</summary>
    Task<LiveData> ReadLiveDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Arıza kodlarını okur (örn. "P0104"). Arıza yoksa liste boş döner.</summary>
    Task<IReadOnlyList<string>> ReadFaultCodesAsync(CancellationToken cancellationToken = default);

    /// <summary>Arıza kodlarını siler.</summary>
    Task ClearFaultCodesAsync(CancellationToken cancellationToken = default);

    /// <summary>Bağlantıyı kapatır.</summary>
    Task DisconnectAsync();
}
