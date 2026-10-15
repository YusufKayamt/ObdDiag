using System.IO.Ports;

namespace ObdDiag.Hardware;

/// <summary>System.IO.Ports ile açılmış gerçek COM port bağlantısı.</summary>
internal sealed class SerialPortConnection : ISerialConnection
{
    private readonly SerialPort _port;

    public SerialPortConnection(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            WriteTimeout = 2000
        };
        _port.Open();
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        // Port kapalıysa/koptuysa Write beklemesin diye ayrı iş parçacığında çalışır.
        return Task.Run(() => _port.Write(data, 0, data.Length), cancellationToken);
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        // SerialPort'un kendi async okuması iptal edilemediği için veri gelene kadar kısa aralıklarla bakıyoruz.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_port.IsOpen)
                throw new IOException("COM port kapandı (cihaz çıkarılmış olabilir).");

            var available = _port.BytesToRead;
            if (available > 0)
                return _port.Read(buffer, 0, Math.Min(available, buffer.Length));

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    public void DiscardInput()
    {
        if (_port.IsOpen)
            _port.DiscardInBuffer();
    }

    public ValueTask DisposeAsync()
    {
        try { if (_port.IsOpen) _port.Close(); } catch { /* kapanırken hata önemsiz */ }
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
