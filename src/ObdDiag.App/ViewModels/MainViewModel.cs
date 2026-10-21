using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObdDiag.App.Services;
using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Models;
using ObdDiag.Core.Parsing;
using ObdDiag.Data;
using ObdDiag.Data.Entities;
using ObdDiag.Hardware;

namespace ObdDiag.App.ViewModels;

/// <summary>
/// Canlı veri ekranının "beyni". Ekran (XAML) sadece buradaki özelliklere ve komutlara bağlanır;
/// cihazla konuşma ObdService üzerinden, arka planda (async) yapılır, böylece ekran donmaz.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string Empty = "—";
    private const int MaxFailuresBeforeDisconnect = 3;

    private readonly IComPortProvider _portProvider;
    private readonly IObdServiceFactory _factory;
    private readonly IDialogService _dialogs;
    private readonly IWorkshopService _workshop;
    private readonly IReportService _reports;

    private IObdService? _service;
    private CancellationTokenSource? _sessionCts;
    private Task? _pollTask;

    // Teşhis kaydı için: son okunan canlı değerler ve arıza kodları
    private LiveData? _lastLiveData;
    private List<string> _readCodes = new();
    private bool _codesClearedAfterRead;

    public MainViewModel(
        IComPortProvider portProvider,
        IObdServiceFactory factory,
        IDialogService dialogs,
        IWorkshopService workshop,
        IReportService reports,
        CustomersViewModel customers,
        SettingsViewModel settings)
    {
        _portProvider = portProvider;
        _factory = factory;
        _dialogs = dialogs;
        _workshop = workshop;
        _reports = reports;
        Customers = customers;
        Settings = settings;

        // Müşteri ekranında araç kaydedilince, canlı ekrandaki araç bilgisi de güncellensin.
        Customers.VehicleSaved += OnVehicleSaved;

        // İlk seçenek varsayılandır: serviste en sık görülen durum, rölantide bekleyen araç.
        Scenarios = new[]
        {
            new ScenarioOption("Rölantide bekleyen araç", FakeScenario.Idle),
            new ScenarioOption("Yolda giden araç", FakeScenario.Normal),
            new ScenarioOption("Arızalı araç (P0104, P0301)", FakeScenario.Faulty),
            new ScenarioOption("Araç bağlı değil", FakeScenario.NotConnected),
            new ScenarioOption("Yavaş cevap veren araç", FakeScenario.Slow),
        };
        _selectedScenario = Scenarios[0];

        RefreshPorts();
        ResetLiveValues();
    }

    /// <summary>"Müşteri ve Araçlar" sekmesinin ViewModel'i.</summary>
    public CustomersViewModel Customers { get; }

    /// <summary>"Ayarlar" sekmesinin ViewModel'i (rapordaki işletme bilgileri).</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Canlı verinin ne sıklıkla yenileneceği.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Açık sekme: 0 = Canlı Veri, 1 = Müşteri ve Araçlar, 2 = Ayarlar.</summary>
    [ObservableProperty] private int _selectedTabIndex;

    // ---- Bağlantı paneli ----

    public ObservableCollection<string> Ports { get; } = new();
    public IReadOnlyList<ScenarioOption> Scenarios { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string? _selectedPort;

    [ObservableProperty]
    private ScenarioOption _selectedScenario;

    // Sahte cihaz, gerçek cihaz yokken denemek için varsayılan olarak açık.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectPort), nameof(CanSelectScenario))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _useFakeDevice = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConnection), nameof(CanSelectPort), nameof(CanSelectScenario))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(ReadFaultsCommand), nameof(ClearFaultsCommand), nameof(SaveDiagnosisCommand), nameof(SaveDiagnosisWithReportCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConnection), nameof(CanSelectPort), nameof(CanSelectScenario))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(ReadFaultsCommand), nameof(ClearFaultsCommand), nameof(SaveDiagnosisCommand), nameof(SaveDiagnosisWithReportCommand))]
    private bool _isBusy;

    public bool CanEditConnection => !IsConnected && !IsBusy;
    public bool CanSelectPort => CanEditConnection && !UseFakeDevice;
    public bool CanSelectScenario => CanEditConnection && UseFakeDevice;

    // ---- Araç tanıma ----

    /// <summary>Şasi numarası kutusu: araçtan otomatik dolar, okunamazsa elle yazılır.</summary>
    [ObservableProperty] private string _vinInput = string.Empty;

    [ObservableProperty] private string _vehicleInfoText = "Araç henüz tanınmadı. Bağlanınca şasi numarası otomatik okunur.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVehicle))]
    [NotifyCanExecuteChangedFor(nameof(SaveDiagnosisCommand), nameof(SaveDiagnosisWithReportCommand))]
    private Vehicle? _currentVehicle;

    /// <summary>VIN geçerli ama kayıtlı değil: "Yeni araç olarak kaydet" düğmesi görünür.</summary>
    [ObservableProperty] private bool _canRegisterVehicle;

    public bool HasVehicle => CurrentVehicle is not null;

    // ---- Canlı veri ----

    [ObservableProperty] private string _rpmText = Empty;
    [ObservableProperty] private string _speedText = Empty;
    [ObservableProperty] private string _coolantText = Empty;
    [ObservableProperty] private string _throttleText = Empty;

    // Göstergelerdeki çubukların dolulukları
    [ObservableProperty] private double _rpmValue;
    [ObservableProperty] private double _speedValue;
    [ObservableProperty] private double _coolantValue;
    [ObservableProperty] private double _throttleValue;

    // ---- Arıza kodları ----

    public ObservableCollection<string> FaultCodes { get; } = new();

    [ObservableProperty]
    private string _faultsSummary = "Henüz arıza kodu okunmadı.";

    // ---- Teşhisi kaydetme ----

    [ObservableProperty] private string _repairNote = string.Empty;
    [ObservableProperty] private string _repairMileage = string.Empty;

    // ---- Durum çubuğu ----

    [ObservableProperty]
    private string _statusMessage = "Hazır. Bağlanmak için \"Bağlan\" düğmesine basın.";

    [ObservableProperty]
    private bool _isStatusError;

    // ================= Komutlar =================

    [RelayCommand]
    private void RefreshPorts()
    {
        var previous = SelectedPort;
        Ports.Clear();
        foreach (var port in _portProvider.GetPortNames())
            Ports.Add(port);

        SelectedPort = previous is not null && Ports.Contains(previous) ? previous : Ports.FirstOrDefault();
    }

    private bool CanConnect() => !IsConnected && !IsBusy && (UseFakeDevice || SelectedPort is not null);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        SetStatus("Bağlanıyor... Lütfen bekleyin.");
        _sessionCts = new CancellationTokenSource();
        var token = _sessionCts.Token;

        var service = _factory.Create(new ConnectionSettings(UseFakeDevice, SelectedPort, SelectedScenario.Value));
        try
        {
            await service.InitializeAsync(token);

            SetStatus("Araç tanınıyor...");
            await IdentifyVehicleAsync(service, token);

            _lastLiveData = null;
            _readCodes = new List<string>();
            _codesClearedAfterRead = false;

            _service = service;
            IsConnected = true;
            SetStatus(UseFakeDevice ? "Sahte cihaza bağlandı." : $"{SelectedPort} üzerinden araca bağlandı.");
            _pollTask = RunPollingAsync(service, token);
        }
        catch (Exception ex)
        {
            await SafeDisconnectAsync(service);
            _sessionCts.Dispose();
            _sessionCts = null;
            SetStatus(Describe(ex), isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task DisconnectAsync()
    {
        await DisconnectCoreAsync(waitForPolling: true);
        SetStatus("Bağlantı kapatıldı.");
    }

    private bool CanUseVehicle() => IsConnected && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUseVehicle))]
    private async Task ReadFaultsAsync()
    {
        var service = _service;
        if (service is null) return;

        IsBusy = true;
        SetStatus("Arıza kodları okunuyor...");
        try
        {
            var codes = await service.ReadFaultCodesAsync(_sessionCts?.Token ?? CancellationToken.None);

            FaultCodes.Clear();
            foreach (var code in codes)
                FaultCodes.Add(code);

            _readCodes = codes.ToList();
            _codesClearedAfterRead = false;

            FaultsSummary = codes.Count == 0 ? "Arıza kodu bulunamadı. ✔" : $"{codes.Count} arıza kodu bulundu.";
            SetStatus("Arıza kodları okundu.");
        }
        catch (Exception ex)
        {
            SetStatus(Describe(ex), isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseVehicle))]
    private async Task ClearFaultsAsync()
    {
        var service = _service;
        if (service is null) return;

        // Silme geri alınamaz, o yüzden önce soruyoruz.
        var approved = _dialogs.Confirm(
            "Arıza Kodlarını Sil",
            "Arıza kodları aracın bilgisayarından silinecek. Bu işlem geri alınamaz.\n\nDevam etmek istiyor musunuz?");
        if (!approved)
        {
            SetStatus("Silme işlemi iptal edildi.");
            return;
        }

        IsBusy = true;
        SetStatus("Arıza kodları siliniyor...");
        try
        {
            await service.ClearFaultCodesAsync(_sessionCts?.Token ?? CancellationToken.None);

            // Okunmuş kodlar tamir kaydına "silindi" notuyla girebilsin diye saklı kalır.
            _codesClearedAfterRead = true;

            FaultCodes.Clear();
            FaultsSummary = "Arıza kodları silindi.";
            SetStatus("Arıza kodları silindi.");
        }
        catch (Exception ex)
        {
            SetStatus(Describe(ex), isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Araç bulma / kaydetme ----

    [RelayCommand]
    private Task FindVehicleAsync() => LookupVehicleAsync(VinInput);

    [RelayCommand]
    private void RegisterVehicle()
    {
        var error = VinValidator.GetError(VinInput);
        if (error is not null)
        {
            VehicleInfoText = error;
            return;
        }

        Customers.PrepareNewVehicle(VinValidator.Normalize(VinInput));
        SelectedTabIndex = 1;
    }

    private bool CanSaveDiagnosis() => IsConnected && !IsBusy && CurrentVehicle is not null;

    /// <summary>Okunan arıza kodlarını ve son canlı değerleri aracın tamir geçmişine kaydeder.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveDiagnosis))]
    private Task SaveDiagnosisAsync() => SaveDiagnosisCoreAsync(withReport: false);

    /// <summary>Teşhisi kaydeder, ardından bu kaydın PDF arıza raporunu oluşturur.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveDiagnosis))]
    private Task SaveDiagnosisWithReportAsync() => SaveDiagnosisCoreAsync(withReport: true);

    private async Task SaveDiagnosisCoreAsync(bool withReport)
    {
        var vehicle = CurrentVehicle;
        if (vehicle is null) return;

        int? mileage = null;
        if (!string.IsNullOrWhiteSpace(RepairMileage))
        {
            if (!int.TryParse(RepairMileage.Replace(".", "").Replace(" ", ""), out var km))
            {
                SetStatus("Kilometre sadece rakamlardan oluşmalı.", isError: true);
                return;
            }
            mileage = km;
        }

        var record = new RepairRecord
        {
            VehicleId = vehicle.Id,
            Date = DateTime.Now,
            Mileage = mileage,
            Description = string.IsNullOrWhiteSpace(RepairNote) ? "Teşhis (arıza kodu ve canlı veri okuma)" : RepairNote.Trim(),
            DtcRecords = _readCodes.Select(c => new DtcRecord { Code = c, WasCleared = _codesClearedAfterRead }).ToList()
        };
        if (_lastLiveData is { } d)
        {
            record.LiveDataSnapshots.Add(new LiveDataSnapshot
            {
                CapturedAt = DateTime.Now,
                Rpm = d.Rpm,
                Speed = d.Speed,
                CoolantTemp = d.CoolantTemp,
                Throttle = d.Throttle
            });
        }

        IsBusy = true;
        RepairRecord saved;
        try
        {
            saved = await _workshop.AddRepairAsync(record);
            RepairNote = string.Empty;
            await Customers.RefreshHistoryAsync(vehicle.Id);
            SetStatus("Teşhis, aracın tamir geçmişine kaydedildi.");
        }
        catch (Exception ex)
        {
            SetStatus(Describe(ex), isError: true);
            IsBusy = false;
            return;
        }

        try
        {
            if (!withReport)
                return;

            if (vehicle.Customer is not { } customer)
            {
                SetStatus("Teşhis kaydedildi ama aracın müşterisi bulunamadığı için rapor oluşturulamadı.", isError: true);
                return;
            }

            SetStatus("Teşhis kaydedildi. PDF rapor hazırlanıyor...");
            var path = await _reports.ExportAsync(customer, vehicle, saved);
            SetStatus(path is null
                ? "Teşhis kaydedildi. PDF rapor oluşturulmadı (kayıt yeri seçilmedi)."
                : $"Teşhis kaydedildi ve PDF rapor oluşturuldu: {path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Teşhis kaydedildi ama PDF rapor oluşturulamadı. {ReportService.DescribeError(ex)}", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pencere kapanırken çağrılır: okumayı durdurur, cihazı serbest bırakır.</summary>
    public void Shutdown()
    {
        _sessionCts?.Cancel();
        var service = Interlocked.Exchange(ref _service, null);
        if (service is not null)
            SafeDisconnectAsync(service).GetAwaiter().GetResult();
    }

    // ================= Araç tanıma =================

    /// <summary>Araçtan VIN okumayı dener; okuyabilirse kayıtlı aracı arar. Okunamazsa bağlantıyı bozmaz.</summary>
    private async Task IdentifyVehicleAsync(IObdService service, CancellationToken token)
    {
        string? vin = null;
        try
        {
            vin = await service.ReadVinAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // VIN okuma desteklenmeyebilir; diğer işlemler yine de çalışsın.
        }

        if (vin is null)
        {
            CurrentVehicle = null;
            CanRegisterVehicle = false;
            VinInput = string.Empty;
            VehicleInfoText = "Araçtan şasi numarası okunamadı (eski araçlarda normaldir). Numarayı aşağıya yazıp \"Aracı Bul\"a basabilirsiniz.";
            return;
        }

        VinInput = vin;
        await LookupVehicleAsync(vin);
    }

    private async Task LookupVehicleAsync(string vinText)
    {
        var error = VinValidator.GetError(vinText);
        if (error is not null)
        {
            CurrentVehicle = null;
            CanRegisterVehicle = false;
            VehicleInfoText = error;
            return;
        }

        var vin = VinValidator.Normalize(vinText);
        try
        {
            var vehicle = await _workshop.FindVehicleByVinAsync(vin);
            CurrentVehicle = vehicle;
            CanRegisterVehicle = vehicle is null;
            VehicleInfoText = vehicle is null
                ? $"Bu şasi numarası kayıtlı değil: {vin}"
                : DescribeVehicle(vehicle);
        }
        catch (Exception ex)
        {
            CurrentVehicle = null;
            CanRegisterVehicle = false;
            VehicleInfoText = $"Kayıtlı araç aranırken sorun oluştu: {ex.Message}";
        }
    }

    private static string DescribeVehicle(Vehicle v)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.Plate)) parts.Add(v.Plate!);
        var car = $"{v.Make} {v.Model}".Trim();
        if (car.Length > 0) parts.Add(car);
        var name = parts.Count > 0 ? string.Join(" — ", parts) : v.Vin;
        return $"Kayıtlı araç: {name}  |  Müşteri: {v.Customer?.FullName ?? "?"}";
    }

    private void OnVehicleSaved(object? sender, Vehicle saved)
    {
        // Şu an ekranda yazılı VIN ile aynı araç kaydedildiyse bilgi kutusunu yenile.
        if (VinValidator.Normalize(VinInput) == saved.Vin)
            _ = LookupVehicleAsync(saved.Vin);
    }

    // ================= Canlı veri döngüsü =================

    /// <summary>Bağlıyken belli aralıkla değerleri okur. Art arda hata olursa bağlantıyı keser.</summary>
    private async Task RunPollingAsync(IObdService service, CancellationToken token)
    {
        var failures = 0;
        var lost = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var data = await service.ReadLiveDataAsync(token);
                if (data.Rpm is null && data.Speed is null && data.CoolantTemp is null && data.Throttle is null)
                    throw new ObdException("Araçtan hiç veri gelmedi.");

                _lastLiveData = data;
                ShowLiveData(data);
                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures >= MaxFailuresBeforeDisconnect)
                {
                    SetStatus($"Bağlantı koptu: {Describe(ex)}", isError: true);
                    lost = true;
                    break;
                }
            }

            try
            {
                await Task.Delay(PollInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (lost)
        {
            // Mesajı koruyoruz; kendi döngümüzü beklememek için waitForPolling: false.
            await DisconnectCoreAsync(waitForPolling: false);
        }
    }

    private void ShowLiveData(LiveData data)
    {
        RpmText = data.Rpm is { } rpm ? rpm.ToString("0") : Empty;
        RpmValue = data.Rpm ?? 0;
        SpeedText = data.Speed is { } speed ? speed.ToString() : Empty;
        SpeedValue = data.Speed ?? 0;
        CoolantText = data.CoolantTemp is { } temp ? temp.ToString() : Empty;
        CoolantValue = Math.Max(0, data.CoolantTemp ?? 0);
        ThrottleText = data.Throttle is { } throttle ? throttle.ToString("0") : Empty;
        ThrottleValue = data.Throttle ?? 0;
    }

    private void ResetLiveValues() => ShowLiveData(new LiveData(null, null, null, null));

    // ================= Yardımcılar =================

    private async Task DisconnectCoreAsync(bool waitForPolling)
    {
        var service = Interlocked.Exchange(ref _service, null);
        _sessionCts?.Cancel();

        if (waitForPolling && _pollTask is not null)
        {
            try { await _pollTask; } catch { /* döngü zaten kendi hatalarını yönetiyor */ }
        }

        if (service is not null)
            await SafeDisconnectAsync(service);

        _sessionCts?.Dispose();
        _sessionCts = null;
        _pollTask = null;

        IsConnected = false;
        ResetLiveValues();
    }

    private static async Task SafeDisconnectAsync(IObdService service)
    {
        try { await service.DisconnectAsync(); }
        catch { /* kapatırken çıkan hata kullanıcıyı ilgilendirmez */ }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    /// <summary>Hatayı kullanıcının anlayacağı sade Türkçe ile açıklar.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        ObdException => ex.Message,
        DataValidationException => ex.Message,
        TimeoutException => ex.Message,
        OperationCanceledException => "İşlem iptal edildi.",
        UnauthorizedAccessException => "Port açılamadı. Başka bir program bu cihazı kullanıyor olabilir; onu kapatıp tekrar deneyin.",
        IOException => "Cihaza ulaşılamadı. Cihazın USB'ye takılı olduğunu ve doğru COM portunu seçtiğinizi kontrol edin.",
        _ => $"Beklenmeyen bir sorun oluştu: {ex.Message}"
    };
}
