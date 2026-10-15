namespace ObdDiag.Hardware;

/// <summary>
/// Açık bir seri bağlantının en yalın hali. Gerçek port (<see cref="SerialPortConnection"/>)
/// ve testlerdeki sahte bağlantı bunu uygular; böylece bağlantı kopma gibi durumlar donanım olmadan test edilir.
/// </summary>
internal interface ISerialConnection : IAsyncDisposable
{
    Task WriteAsync(byte[] data, CancellationToken cancellationToken);

    /// <summary>En az 1 bayt gelene kadar bekler. 0 dönerse bağlantı kapanmıştır.</summary>
    Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);

    /// <summary>Gelmiş ama okunmamış eski verileri atar.</summary>
    void DiscardInput();
}
