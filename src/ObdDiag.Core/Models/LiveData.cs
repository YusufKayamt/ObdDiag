namespace ObdDiag.Core.Models;

/// <summary>
/// Araçtan okunan anlık değerler. Bir değer okunamadıysa (örn. araç o veriyi desteklemiyorsa) null olur.
/// </summary>
/// <param name="Rpm">Motor devri (devir/dakika)</param>
/// <param name="Speed">Araç hızı (km/s)</param>
/// <param name="CoolantTemp">Soğutma suyu sıcaklığı (°C)</param>
/// <param name="Throttle">Gaz pedalı / kelebek konumu (%)</param>
public sealed record LiveData(double? Rpm, int? Speed, int? CoolantTemp, double? Throttle);
