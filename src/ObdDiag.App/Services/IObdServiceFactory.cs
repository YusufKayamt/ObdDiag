using ObdDiag.Core.Interfaces;
using ObdDiag.Hardware;

namespace ObdDiag.App.Services;

/// <summary>Bağlantı paneline girilen seçimler.</summary>
/// <param name="UseFakeDevice">Sahte cihaz kullanılsın mı?</param>
/// <param name="PortName">Gerçek cihaz için COM port adı (örn. "COM3").</param>
/// <param name="Scenario">Sahte cihaz için taklit edilecek durum.</param>
public sealed record ConnectionSettings(bool UseFakeDevice, string? PortName, FakeScenario Scenario);

/// <summary>Seçime göre gerçek veya sahte cihazla çalışan <see cref="IObdService"/> üretir.</summary>
public interface IObdServiceFactory
{
    IObdService Create(ConnectionSettings settings);
}
