using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Models;
using ObdDiag.Core.Parsing;

namespace ObdDiag.Core.Services;

/// <summary>
/// <see cref="IElmTransport"/> üzerinden komut gönderip cevapları çözücülerle anlamlı değere çevirir.
/// Gerçek ya da sahte cihaz fark etmez.
/// </summary>
public sealed class ObdService : IObdService
{
    /// <summary>Başlatma komutları, bu sırayla gönderilir.</summary>
    public static readonly IReadOnlyList<string> InitCommands = new[] { "ATZ", "ATE0", "ATL0", "ATS0", "ATH0", "ATSP0" };

    private readonly IElmTransport _transport;

    public ObdService(IElmTransport transport)
    {
        _transport = transport;
    }

    public bool IsInitialized { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsInitialized = false;

        if (!_transport.IsConnected)
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

        foreach (var command in InitCommands)
        {
            var answer = await _transport.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            if (ElmResponse.GetLines(answer).Contains("?"))
                throw new ObdException($"Cihaz '{command}' komutunu anlamadı. Takılı cihazın ELM327 uyumlu olduğundan emin olun.");
        }

        // "0100" = "hangi veriler destekleniyor?" sorusu. Araç cevap veriyorsa bağlantı kurulmuştur.
        var probe = await _transport.SendCommandAsync("0100", cancellationToken).ConfigureAwait(false);
        if (!ElmResponse.TryGetPidData(probe, 0x00, out _))
            throw new ObdException(DescribeConnectionFailure(probe));

        IsInitialized = true;
    }

    public async Task<LiveData> ReadLiveDataAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // Cihaz aynı anda tek komut işler, bu yüzden sırayla soruyoruz.
        double? rpm = null;
        int? speed = null;
        int? coolant = null;
        double? throttle = null;

        if (PidParser.TryParseRpm(await AskAsync("010C", cancellationToken).ConfigureAwait(false), out var r)) rpm = r;
        if (PidParser.TryParseSpeed(await AskAsync("010D", cancellationToken).ConfigureAwait(false), out var s)) speed = s;
        if (PidParser.TryParseCoolantTemp(await AskAsync("0105", cancellationToken).ConfigureAwait(false), out var c)) coolant = c;
        if (PidParser.TryParseThrottle(await AskAsync("0111", cancellationToken).ConfigureAwait(false), out var t)) throttle = t;

        return new LiveData(rpm, speed, coolant, throttle);
    }

    public async Task<IReadOnlyList<string>> ReadFaultCodesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var answer = await AskAsync("03", cancellationToken).ConfigureAwait(false);
        if (!DtcParser.TryParse(answer, out var codes))
            throw new ObdException("Arıza kodları okunamadı. Araç cevap vermedi veya cevap anlaşılamadı. Biraz bekleyip tekrar deneyin.");

        return codes;
    }

    public async Task ClearFaultCodesAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var answer = await AskAsync("04", cancellationToken).ConfigureAwait(false);
        if (!DtcParser.IsClearConfirmed(answer))
            throw new ObdException("Arıza kodları silinemedi. Araç silme isteğini onaylamadı. Kontağın açık olduğundan emin olun.");
    }

    public async Task DisconnectAsync()
    {
        IsInitialized = false;
        await _transport.DisconnectAsync().ConfigureAwait(false);
    }

    private Task<string> AskAsync(string command, CancellationToken cancellationToken) =>
        _transport.SendCommandAsync(command, cancellationToken);

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Önce araca bağlanmalısınız (InitializeAsync).");
    }

    /// <summary>Araç cevap vermediyse nedenini sade Türkçe ile açıklar.</summary>
    private static string DescribeConnectionFailure(string answer)
    {
        // GetLines boşlukları sildiği için karşılaştırma boşluksuz yazılır.
        var lines = ElmResponse.GetLines(answer);

        if (lines.Contains("UNABLETOCONNECT"))
            return "Araca bağlanılamadı. Cihazın OBD soketine tam takıldığından ve kontağın açık olduğundan emin olun.";
        if (lines.Contains("NODATA"))
            return "Araç cevap vermedi. Kontağı açıp birkaç saniye bekleyin, sonra tekrar deneyin.";
        if (lines.Contains("STOPPED"))
            return "Bağlantı yarıda kesildi. Tekrar deneyin.";

        return "Araçtan anlaşılır bir cevap alınamadı. Cihazı çıkarıp yeniden takın ve tekrar deneyin.";
    }
}
