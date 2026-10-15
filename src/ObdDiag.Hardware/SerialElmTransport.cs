using System.Text;
using ObdDiag.Core.Interfaces;

namespace ObdDiag.Hardware;

/// <summary>
/// Gerçek ELM327 cihazıyla seri port (USB) üzerinden konuşur.
/// <list type="bullet">
/// <item>Cevap, '&gt;' işareti gelene kadar okunur.</item>
/// <item>Cevap gelmezse zaman aşımı olur (<see cref="ResponseTimeout"/>).</item>
/// <item>Aynı anda tek komut işlenir; diğerleri sırada bekler.</item>
/// <item>Bağlantı koparsa aynı komut için port yeniden açılıp birkaç kez denenir.</item>
/// </list>
/// </summary>
public sealed class SerialElmTransport : IElmTransport
{
    private const int MaxResponseLength = 4096;

    private readonly Func<ISerialConnection> _connectionFactory;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private ISerialConnection? _connection;
    private volatile bool _connectRequested;

    /// <param name="portName">Örn. "COM3".</param>
    /// <param name="baudRate">Hız. Çoğu ELM327 için 38400, bazıları 115200 veya 9600 kullanır.</param>
    public SerialElmTransport(string portName, int baudRate = 38400)
        : this(() => new SerialPortConnection(portName, baudRate))
    {
    }

    // Testlerde sahte bağlantı vermek için.
    internal SerialElmTransport(Func<ISerialConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>Bir komuta cevap için en fazla beklenecek süre. İlk araç aramasında cihaz uzun düşünebilir.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Bağlantı koparsa bir komut için kaç kez yeniden bağlanıp deneneceği.</summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>Yeniden denemeler arasındaki bekleme.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public bool IsConnected => _connectRequested && _connection is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
                _connection = _connectionFactory();
            _connectRequested = true;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _connectRequested = false;
        await CloseConnectionAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (!_connectRequested)
            throw new InvalidOperationException("Cihaza bağlı değilsiniz. Önce ConnectAsync çağırın.");

        // Sıra: aynı anda sadece bir komut cihaza gider.
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    _connection ??= _connectionFactory();
                    return await ExchangeAsync(_connection, command.Trim(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsConnectionLost(ex)
                                           && _connectRequested
                                           && attempt < MaxReconnectAttempts
                                           && !cancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    await CloseConnectionAsync().ConfigureAwait(false);
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>Komutu yazar, '&gt;' gelene kadar okur.</summary>
    private async Task<string> ExchangeAsync(ISerialConnection connection, string command, CancellationToken cancellationToken)
    {
        // Önceki komuttan kalmış eski veri bu cevaba karışmasın.
        connection.DiscardInput();

        await connection.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResponseTimeout);

        var buffer = new byte[256];
        var text = new StringBuilder();

        try
        {
            while (true)
            {
                var count = await connection.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                if (count == 0)
                    throw new IOException("Cihazla bağlantı koptu.");

                // Bazı cihazlar araya boş (NUL) karakter koyar; atıyoruz.
                for (var i = 0; i < count; i++)
                    if (buffer[i] != 0)
                        text.Append((char)buffer[i]);

                var end = text.ToString().IndexOf('>');
                if (end >= 0)
                    return text.ToString(0, end + 1);

                if (text.Length > MaxResponseLength)
                    throw new InvalidDataException("Cihazdan beklenmedik uzunlukta cevap geldi.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Cihaz {ResponseTimeout.TotalSeconds:0} saniye içinde cevap vermedi. Cihazın takılı olduğunu ve doğru COM portunu seçtiğinizi kontrol edin.");
        }
    }

    private static bool IsConnectionLost(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException;

    private async Task CloseConnectionAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
        {
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch { /* kapanırken hata önemsiz */ }
        }
    }
}
