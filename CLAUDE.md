# PROJE TANIMI (Yapay zekâ asistanı için talimat dosyası)

Proje: ELM327 Entegrasyonlu, Yerel Veritabanı Destekli B2B Oto Diagnostik ve İşletme Yönetim Masaüstü Yazılımı
Öğrenci: Yusuf Mete KAYA — Fırat Üniversitesi Yazılım Mühendisliği bitirme projesi.
Hedef kullanıcı: Oto sanayideki bağımsız servisler ve oto elektrikçiler.

Program ne yapacak:
1. ELM327 USB cihazı ile arabanın beynine (ECU) bağlanacak, canlı veri okuyacak (devir, hız, motor sıcaklığı, gaz pedalı).
2. Arıza kodlarını (DTC, örn. P0104) okuyacak, açıklamasını Türkçe gösterecek, istenirse silecek.
3. Müşteri ve araç kaydı tutacak (şasi numarası / VIN ile), her aracın tamir geçmişini saklayacak.
4. İşlem sonunda işletme logolu PDF arıza raporu üretecek.
5. Gerçek araba olmadan da test edilebilmesi için "Sahte ELM327" (Mock) modu olacak.

---

## ÖNEMLİ: KULLANICI HAKKINDA

Kullanıcının teknik bilgisi ZAYIF. Bu yüzden:
- Her zaman basit, sade Türkçe ile konuş. Teknik terim kullanırsan hemen yanında basitçe açıkla.
- Kullanıcıdan terminal komutu yazmasını isteme; gereken komutları SEN çalıştır.
- Git işlemlerini (commit) SEN yönet. Her adım bitince değişiklikleri anlamlı bir mesajla commit et.
- Her adımın sonunda kullanıcıya şunları kısaca söyle:
  1. Bu adımda ne yaptın (2-3 cümle, sade dille),
  2. Programı nasıl açıp deneyeceğini (tıklanacak yerler dahil),
  3. Ekranda ne görmesi gerektiğini,
  4. Sıradaki adımın numarası.
- Kullanıcı "X. adımı yap" dediğinde aşağıdaki ADIMLAR listesindeki o adımı uygula.
- Kullanıcı bir hata mesajı yapıştırırsa, sebebini basitçe açıkla ve düzelt.
- Kullanıcı "bunu bana açıkla" derse, kodu bir lise öğrencisine anlatır gibi, örneklerle açıkla
  (kullanıcı bu projeyi jüriye savunacak).

---

## TEKNOLOJİ

- .NET 8, C#, WPF (net8.0-windows), MVVM için CommunityToolkit.Mvvm
- Seri port: System.IO.Ports (NuGet)
- Veritabanı: SQLite + Entity Framework Core
- PDF: QuestPDF (Community lisansı)
- Testler: xUnit + FluentAssertions + Moq
- Dependency Injection: Microsoft.Extensions.DependencyInjection / Hosting

## KLASÖR YAPISI

```
ObdDiag.sln
src/
  ObdDiag.Core/        modeller, arayüzler (IElmTransport, IObdService), PID ve DTC çözücüler. Başka projeye bağımlı DEĞİL.
  ObdDiag.Hardware/    SerialElmTransport (gerçek cihaz), FakeElmTransport (sahte cihaz)
  ObdDiag.Data/        EF Core DbContext ve tablolar
  ObdDiag.Reporting/   QuestPDF rapor
  ObdDiag.App/         WPF arayüz
tests/
  ObdDiag.Core.Tests/
  ObdDiag.Hardware.Tests/
  ObdDiag.Data.Tests/
docs/                  tez için diyagramlar ve açıklamalar
```

## MİMARİ KURALLAR

- Arayüz (App) seri porta ASLA doğrudan erişmez; her şey IElmTransport arayüzü üzerinden gider.
- FakeElmTransport, gerçek ELM327 ile birebir aynı formatta cevap verir (sonundaki '>' işareti dahil).
- Tüm cihaz iletişimi async + CancellationToken; arayüz asla donmamalı.
- Her değişiklikten sonra `dotnet build` ve `dotnet test` çalıştır. Testler geçmeden adımı bitmiş sayma.
- Kod içindeki isimler İngilizce; yorumlar, ekran yazıları ve kullanıcıya mesajlar Türkçe.
- Emin olmadığın bir protokol detayını uydurma; kullanıcıya sor.

## ELM327 / OBD2 BİLGİLERİ

- Başlatma komutları sırası: ATZ, ATE0, ATL0, ATS0, ATH0, ATSP0
- Cevap, '>' karakteri gelene kadar okunur.
- Özel cevaplar: "SEARCHING...", "NO DATA", "UNABLE TO CONNECT", "?", "STOPPED", "BUS INIT: ...OK"
- Formüller (A ve B cevaptaki veri baytları):
  - 010C Motor devri (RPM) = (256*A + B) / 4
  - 010D Araç hızı = A km/s
  - 0105 Soğutma suyu sıcaklığı = A - 40 °C
  - 0111 Gaz kelebeği konumu = A * 100 / 255 %
- Mod 03 = arıza kodlarını oku, Mod 04 = arıza kodlarını sil, Mod 09 PID 02 = şasi no (VIN)
- DTC çözümleme: her kod 2 bayttır. İlk baytın en üst 2 biti harfi verir (00=P, 01=C, 10=B, 11=U),
  sonraki 2 bit 1. rakamı, kalan 3 nibble hex rakamlardır. Örnek: "01 04" -> P0104.
  CAN protokolünde "43"ten sonra kod sayısını gösteren bir bayt daha gelir; iki formatı da destekle.
- Örnek test cevapları:
  - "41 0C 1A F8" -> 1726 rpm
  - "41 05 7B" -> 83 °C
  - "43 01 04 00 00 00 00" -> [P0104]
  - "43 02 01 04 03 01" (CAN) -> [P0104, P0301]

---

## ADIMLAR (Kullanıcı "1. adımı yap", "2. adımı yap" diye ilerleyecek)

### 1. adım — Proje iskeleti
Yukarıdaki klasör yapısını oluştur, solution ve projeleri kur, NuGet paketlerini ekle,
boş bir ana pencere açılsın. .gitignore ekle, git deposunu başlat, ilk commit'i yap.
Kullanıcıya programı nasıl çalıştıracağını göster.

### 2. adım — Sahte ELM327 (Mock)
IElmTransport arayüzünü ve FakeElmTransport sınıfını yaz. Senaryolar:
"Normal araç", "Arızalı araç (P0104, P0301)", "Araç bağlı değil (UNABLE TO CONNECT)", "Yavaş cevap veren araç".
Devir ve hız zamanla gerçekçi şekilde değişsin. Testlerini yaz.

### 3. adım — Veri çözücüler (Hex -> anlamlı değer)
Önce testleri yaz, sonra kodu. PidParser (devir, hız, sıcaklık, gaz) ve DtcParser (arıza kodları).
Yukarıdaki örnek cevapları, boşluksuz cevapları, "NO DATA" ve bozuk verileri test et.
Bit işlemleri ile çözümle. Bitince kullanıcıya bit işlemlerini basit bir örnekle açıkla.

### 4. adım — Gerçek seri port bağlantısı
SerialElmTransport: '>' gelene kadar asenkron okuma, zaman aşımı, aynı anda tek komut (kuyruk),
bağlantı koparsa yeniden deneme, bilgisayardaki COM portlarını listeleme.
ObdService başlatma komutlarını sırayla göndersin.

### 5. adım — Canlı veri ekranı
Bağlantı paneli (COM port seçimi + "Sahte cihaz kullan" kutucuğu), canlı devir/hız/sıcaklık/gaz göstergeleri
(arayüz donmadan güncellensin), "Arızaları Oku" ve "Arızaları Sil" butonları. Silmeden önce onay sorusu sor.

### 6. adım — Veritabanı ve müşteri kayıtları
SQLite + EF Core. Tablolar: Customer, Vehicle (VIN benzersiz), RepairRecord, DtcRecord, LiveDataSnapshot.
Müşteri/araç kayıt ekranı. VIN doğrulama (17 karakter, I/O/Q harfi yok).
Araç bağlanınca VIN'i otomatik oku, kayıtlı aracı bul. Araç tamir geçmişi listesi. Testler.
docs/ER.md içine Mermaid ile ER diyagramı çiz (tez için).

### 7. adım — PDF arıza raporu
QuestPDF ile işletme logolu rapor: müşteri, araç, VIN, tarih, arıza kodları ve açıklamaları,
canlı veri özeti, yapılan işlem, imza alanı. Ayarlar ekranından işletme adı/adresi/logosu girilebilsin.

### 8. adım — Türkçe arıza kodu açıklamaları
Yaygın P0xxx kodlarının Türkçe açıklamalarını içeren bir veri dosyası oluştur; ekranda ve raporda kullan.

### 9. adım — Son düzenlemeler
Arayüzü toparla (tutarlı renkler, simgeler), hata mesajlarını kullanıcı dostu yap,
README.md ve docs/KULLANIM_KILAVUZU.md yaz, tüm testlerin geçtiğini doğrula.
