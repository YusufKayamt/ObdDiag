using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObdDiag.App.Services;
using ObdDiag.Core.Interfaces;
using ObdDiag.Core.Models;
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

    private IObdService? _service;
    private CancellationTokenSource? _sessionCts;
    private Task? _pollTask;

    public MainViewModel(IComPortProvider portProvider, IObdServiceFactory factory, IDialogService dialogs)
    {
        _portProvider = portProvider;
        _factory = factory;
        _dialogs = dialogs;

        Scenarios = new[]
        {
            new ScenarioOption("Normal araç", FakeScenario.Normal),
            new ScenarioOption("Arızalı araç (P0104, P0301)", FakeScenario.Faulty),
            new ScenarioOption("Araç bağlı değil", FakeScenario.NotConnected),
            new ScenarioOption("Yavaş cevap veren araç", FakeScenario.Slow),
        };
        _selectedScenario = Scenarios[0];

        RefreshPorts();
        ResetLiveValues();
    }

    /// <summary>Canlı verinin ne sıklıkla yenileneceği.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

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
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(ReadFaultsCommand), nameof(ClearFaultsCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditConnection), nameof(CanSelectPort), nameof(CanSelectScenario))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(ReadFaultsCommand), nameof(ClearFaultsCommand))]
    private bool _isBusy;

    public bool CanEditConnection => !IsConnected && !IsBusy;
    public bool CanSelectPort => CanEditConnection && !UseFakeDevice;
    public bool CanSelectScenario => CanEditConnection && UseFakeDevice;

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

    // ---- Durum çubuğu ----

    [ObservableProperty]
    private string _statusMessage = "Hazır. Bağlanmak için \"Bağlan\" düğmesine basın.";

    [ObservableProperty]
    private bool _isStatusError;

    // ---- Komutlar ----

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

    /// <summary>Pencere kapanırken çağrılır: okumayı durdurur, cihazı serbest bırakır.</summary>
    public void Shutdown()
    {
        _sessionCts?.Cancel();
        var service = Interlocked.Exchange(ref _service, null);
        if (service is not null)
            SafeDisconnectAsync(service).GetAwaiter().GetResult();
    }

    // ---- Canlı veri döngüsü ----

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

    // ---- Yardımcılar ----

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
        TimeoutException => ex.Message,
        OperationCanceledException => "İşlem iptal edildi.",
        UnauthorizedAccessException => "Port açılamadı. Başka bir program bu cihazı kullanıyor olabilir; onu kapatıp tekrar deneyin.",
        IOException => "Cihaza ulaşılamadı. Cihazın USB'ye takılı olduğunu ve doğru COM portunu seçtiğinizi kontrol edin.",
        _ => $"Beklenmeyen bir sorun oluştu: {ex.Message}"
    };
}
