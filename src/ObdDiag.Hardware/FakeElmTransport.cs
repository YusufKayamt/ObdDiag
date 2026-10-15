using System.Diagnostics;
using System.Text;
using ObdDiag.Core.Interfaces;

namespace ObdDiag.Hardware;

/// <summary>
/// Sahte ELM327 cihazı. Gerçek araba olmadan programı denemek için kullanılır.
/// Cevaplar gerçek cihazla aynı biçimdedir: satırlar '\r' ile ayrılır, sonda "\r\r&gt;" gelir.
/// </summary>
/// <remarks>
/// Basitleştirmeler: ATL ve ATH komutları "OK" döner ama çıktıyı değiştirmez (başlıklar hep kapalı kabul edilir).
/// Şasi no (Mod 09) henüz yok; 6. adımda eklenecek. Desteklenmeyen PID'ler "NO DATA" döner.
/// </remarks>
public sealed class FakeElmTransport : IElmTransport
{
    private const string Version = "ELM327 v1.5";

    private readonly FakeScenario _scenario;
    private readonly Func<TimeSpan> _clock;
    private readonly TimeSpan _slowDelay;
    private readonly SemaphoreSlim _commandLock = new(1, 1); // aynı anda tek komut, gerçek cihaz gibi

    private bool _connected;
    private bool _echo = true;
    private bool _spaces = true;
    private bool _protocolFound;
    private List<byte> _faultBytes;

    /// <param name="scenario">Taklit edilecek durum.</param>
    /// <param name="clock">Geçen süre sayacı. Verir ki testlerde zamanı elle ilerletebilelim; boşsa gerçek saat kullanılır.</param>
    /// <param name="slowDelay">"Yavaş" senaryosunda her komuttaki bekleme süresi (varsayılan 1,2 sn).</param>
    public FakeElmTransport(FakeScenario scenario = FakeScenario.Normal, Func<TimeSpan>? clock = null, TimeSpan? slowDelay = null)
    {
        _scenario = scenario;
        _slowDelay = slowDelay ?? TimeSpan.FromMilliseconds(1200);
        if (clock is null)
        {
            var watch = Stopwatch.StartNew();
            clock = () => watch.Elapsed;
        }
        _clock = clock;
        _faultBytes = InitialFaultBytes();
    }

    public bool IsConnected => _connected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _connected = false;
        _commandLock.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!_connected)
            throw new InvalidOperationException("Cihaza bağlı değilsiniz. Önce ConnectAsync çağırın.");

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_scenario == FakeScenario.Slow)
                await Task.Delay(_slowDelay, cancellationToken).ConfigureAwait(false);

            return BuildResponse(command.Trim());
        }
        finally
        {
            _commandLock.Release();
        }
    }

    // ---- Komut işleme ----

    private string BuildResponse(string rawCommand)
    {
        var cmd = rawCommand.Replace(" ", "").ToUpperInvariant();

        if (cmd.StartsWith("AT", StringComparison.Ordinal))
            return HandleAt(rawCommand, cmd);

        if (cmd.Length < 2 || cmd.Length % 2 != 0 || !cmd.All(Uri.IsHexDigit))
            return Format(rawCommand, "?");

        // Araç bağlı değilse her OBD komutu aynı cevabı alır.
        if (_scenario == FakeScenario.NotConnected)
            return Format(rawCommand, "SEARCHING...", "UNABLE TO CONNECT");

        var lines = new List<string>();
        if (!_protocolFound)
        {
            // Gerçek cihaz, ilk OBD komutunda protokolü aramak için biraz uğraşır.
            lines.Add("SEARCHING...");
            _protocolFound = true;
        }

        lines.Add(HandleObd(cmd));
        return Format(rawCommand, lines.ToArray());
    }

    private string HandleAt(string rawCommand, string cmd)
    {
        switch (cmd)
        {
            case "ATZ":
                // Sıfırlama: ayarlar fabrika değerlerine döner (yankı açık, boşluk açık).
                _echo = true;
                _spaces = true;
                _protocolFound = false;
                return Format(rawCommand, Version);
            case "ATI":
                return Format(rawCommand, Version);
            case "ATRV":
                return Format(rawCommand, "12.4V");
            case "ATE0": _echo = false; return Format(rawCommand, "OK");
            case "ATE1": _echo = true; return Format(rawCommand, "OK");
            case "ATS0": _spaces = false; return Format(rawCommand, "OK");
            case "ATS1": _spaces = true; return Format(rawCommand, "OK");
            case "ATL0":
            case "ATL1":
            case "ATH0":
            case "ATH1":
                return Format(rawCommand, "OK");
        }

        if (cmd.StartsWith("ATSP", StringComparison.Ordinal) && cmd.Length == 5)
        {
            _protocolFound = false; // protokol yeniden aranacak
            return Format(rawCommand, "OK");
        }

        return Format(rawCommand, "?");
    }

    /// <summary>OBD komutunun cevap satırını üretir (yankı ve '&gt;' hariç).</summary>
    private string HandleObd(string cmd)
    {
        switch (cmd)
        {
            case "0100":
                // Desteklenen PID'ler (01-20): 04, 05, 0C, 0D, 11
                return Bytes(0x41, 0x00, 0x18, 0x18, 0x80, 0x00);
            case "0105":
                return Bytes(0x41, 0x05, (byte)(CoolantTemp() + 40));
            case "010C":
                var raw = (int)Math.Round(Rpm() * 4); // RPM = (256*A + B) / 4
                return Bytes(0x41, 0x0C, (byte)(raw >> 8), (byte)(raw & 0xFF));
            case "010D":
                return Bytes(0x41, 0x0D, (byte)Speed());
            case "0111":
                return Bytes(0x41, 0x11, (byte)Math.Round(Throttle() * 255 / 100.0));
            case "03":
                return ReadFaultCodes();
            case "04":
                _faultBytes.Clear();
                return Bytes(0x44);
            default:
                return "NO DATA";
        }
    }

    private string ReadFaultCodes()
    {
        // CAN biçimi: "43", sonra kod sayısı, sonra her kod için 2 bayt.
        var data = new List<byte> { 0x43, (byte)(_faultBytes.Count / 2) };
        data.AddRange(_faultBytes);
        return Bytes(data.ToArray());
    }

    private List<byte> InitialFaultBytes() =>
        _scenario == FakeScenario.Faulty
            ? new List<byte> { 0x01, 0x04, 0x03, 0x01 } // P0104, P0301
            : new List<byte>();

    // ---- Zamanla değişen değerler (rastgelelik yok, testler tutarlı olsun diye) ----

    private double Seconds => _clock().TotalSeconds;

    /// <summary>Hız 10-110 km/s arasında 40 saniyede bir gidip gelir.</summary>
    private int Speed() => (int)Math.Round(60 + 50 * Math.Sin(2 * Math.PI * Seconds / 40));

    /// <summary>Devir hıza bağlı artar, üstüne küçük bir dalgalanma biner.</summary>
    private double Rpm() => 800 + Speed() * 22 + 150 * Math.Sin(2 * Math.PI * Seconds / 7);

    /// <summary>Motor 30 °C'den başlayıp yavaşça 90 °C'ye ısınır (5 dakikada).</summary>
    private int CoolantTemp() => (int)Math.Min(90, 30 + Seconds * 0.2);

    /// <summary>Gaz pedalı yüzdesi hıza göre artar.</summary>
    private double Throttle() => Math.Clamp(10 + Speed() * 0.4 + 5 * Math.Sin(2 * Math.PI * Seconds / 3), 0, 100);

    // ---- Cevap biçimlendirme ----

    private string Bytes(params byte[] data) =>
        string.Join(_spaces ? " " : "", data.Select(b => b.ToString("X2")));

    /// <summary>Cevabı ELM327 biçiminde birleştirir: [yankı] satırlar '\r' ile, sonda "\r\r&gt;".</summary>
    private string Format(string rawCommand, params string[] lines)
    {
        var sb = new StringBuilder();
        if (_echo)
            sb.Append(rawCommand).Append('\r');
        sb.Append(string.Join("\r", lines));
        sb.Append("\r\r>");
        return sb.ToString();
    }
}
