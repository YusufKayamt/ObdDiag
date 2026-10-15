using System.Text;
using System.Threading.Channels;
using ObdDiag.Hardware;

namespace ObdDiag.Hardware.Tests;

/// <summary>
/// Testlerde gerçek COM port yerine kullanılan bağlantı. Bir komut yazılınca
/// <see cref="Responder"/> cevabı parçalar halinde "cihazdan gelmiş" gibi sıraya koyar.
/// </summary>
internal sealed class FakeSerialConnection : ISerialConnection
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();

    /// <summary>Yazılan komutu (örn. "010C") alır; cevap parçalarını döner. Null = hiç cevap gelmesin.</summary>
    public Func<string, Task<string[]?>> Responder { get; set; } = _ => Task.FromResult<string[]?>(null);

    /// <summary>Dolu ise yazarken bu hata fırlatılır (kablo çekildi gibi).</summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>True ise okuma 0 döner (bağlantı kapandı).</summary>
    public bool ClosedOnRead { get; set; }

    public bool Disposed { get; private set; }
    public List<string> WrittenCommands { get; } = new();

    public void Enqueue(string chunk) => _incoming.Writer.TryWrite(Encoding.ASCII.GetBytes(chunk));

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (WriteFailure is not null)
            throw WriteFailure;

        var command = Encoding.ASCII.GetString(data).TrimEnd('\r');
        WrittenCommands.Add(command);

        var chunks = await Responder(command);
        if (chunks is null)
            return;
        foreach (var chunk in chunks)
            Enqueue(chunk);
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (ClosedOnRead)
            return 0;

        var chunk = await _incoming.Reader.ReadAsync(cancellationToken);
        chunk.CopyTo(buffer, 0);
        return chunk.Length;
    }

    public void DiscardInput()
    {
        while (_incoming.Reader.TryRead(out _)) { }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
