namespace ObdDiag.Hardware;

/// <summary>Sahte cihazın taklit ettiği durumlar.</summary>
public enum FakeScenario
{
    /// <summary>Yolda giden sağlıklı araç: hız ve devir sürekli değişir, arıza kodu yok.</summary>
    Normal,

    /// <summary>Arızalı araç: P0104 ve P0301 kodları var (silinirse kaybolur).</summary>
    Faulty,

    /// <summary>Cihaz takılı ama araç bağlı değil: "UNABLE TO CONNECT" cevabı verir.</summary>
    NotConnected,

    /// <summary>Yavaş cevap veren araç: her komuta geç cevap verir.</summary>
    Slow,

    /// <summary>
    /// Serviste rölantide bekleyen araç: hız 0, gaza basılmıyor.
    /// Motor soğukken devir biraz yüksektir (~1050), ısındıkça ~800'e iner. Arıza kodu yok.
    /// </summary>
    Idle
}
