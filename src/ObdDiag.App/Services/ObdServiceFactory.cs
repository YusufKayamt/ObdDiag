using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Services;
using ObdDiag.Hardware;

namespace ObdDiag.App.Services;

public sealed class ObdServiceFactory : IObdServiceFactory
{
    public IObdService Create(ConnectionSettings settings)
    {
        // Arayüz hangi cihaz olduğunu bilmez; sadece IElmTransport kuralı üzerinden konuşur.
        IElmTransport transport = settings.UseFakeDevice
            ? new FakeElmTransport(settings.Scenario)
            : new SerialElmTransport(settings.PortName
                ?? throw new InvalidOperationException("COM port seçilmedi."));

        return new ObdService(transport);
    }
}
