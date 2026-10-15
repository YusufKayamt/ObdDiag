using System.IO.Ports;
using ObdDiag.Core.Interfaces;

namespace ObdDiag.Hardware;

/// <summary>Bilgisayardaki COM portlarını listeler.</summary>
public sealed class SerialPortProvider : IComPortProvider
{
    public IReadOnlyList<string> GetPortNames() =>
        SerialPort.GetPortNames()
            .OrderBy(PortNumber)          // COM2, COM10'dan önce gelsin (düz alfabetik sıralamada tersi olur)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int PortNumber(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }
}
