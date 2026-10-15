namespace ObdDiag.Hardware;

/// <summary>Sahte cihazın taklit ettiği durumlar.</summary>
public enum FakeScenario
{
    /// <summary>Sağlıklı araç: arıza kodu yok.</summary>
    Normal,

    /// <summary>Arızalı araç: P0104 ve P0301 kodları var (silinirse kaybolur).</summary>
    Faulty,

    /// <summary>Cihaz takılı ama araç bağlı değil: "UNABLE TO CONNECT" cevabı verir.</summary>
    NotConnected,

    /// <summary>Yavaş cevap veren araç: her komuta geç cevap verir.</summary>
    Slow
}
