using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObdDiag.App.Services;
using ObdDiag.Data;
using ObdDiag.Data.Entities;

namespace ObdDiag.App.ViewModels;

/// <summary>
/// "Müşteri ve Araçlar" ekranının beyni: müşteri ve araç kayıtları (ekle/düzenle/sil/ara)
/// ve seçili aracın tamir geçmişi.
/// </summary>
public sealed partial class CustomersViewModel : ObservableObject
{
    private readonly IWorkshopService _workshop;
    private readonly IDialogService _dialogs;

    // Liste yenilenirken listenin seçimi geçici olarak boşalır; o sırada tepki vermemek için.
    private bool _loadingCustomers;
    private bool _loadingVehicles;

    // Canlı ekrandan gelen, henüz kaydedilmemiş VIN. Müşteri seçilince araç formu temizlense de bu geri yazılır.
    private string? _pendingVin;

    public CustomersViewModel(IWorkshopService workshop, IDialogService dialogs)
    {
        _workshop = workshop;
        _dialogs = dialogs;
    }

    /// <summary>Bir araç kaydedilince tetiklenir (canlı veri ekranı kendini güncellesin diye).</summary>
    public event EventHandler<Vehicle>? VehicleSaved;

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<Vehicle> Vehicles { get; } = new();
    public ObservableCollection<RepairHistoryItem> History { get; } = new();

    // ---- Arama ve seçimler ----

    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCustomerCommand), nameof(SaveVehicleCommand))]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteVehicleCommand), nameof(AddRepairCommand))]
    private Vehicle? _selectedVehicle;

    // ---- Müşteri formu ----

    [ObservableProperty] private string _customerName = string.Empty;
    [ObservableProperty] private string _customerPhone = string.Empty;
    [ObservableProperty] private string _customerEmail = string.Empty;
    [ObservableProperty] private string _customerAddress = string.Empty;
    [ObservableProperty] private string _customerNotes = string.Empty;

    // ---- Araç formu ----

    [ObservableProperty] private string _vehicleVin = string.Empty;
    [ObservableProperty] private string _vehiclePlate = string.Empty;
    [ObservableProperty] private string _vehicleMake = string.Empty;
    [ObservableProperty] private string _vehicleModel = string.Empty;
    [ObservableProperty] private string _vehicleYear = string.Empty;
    [ObservableProperty] private string _vehicleNotes = string.Empty;

    // ---- Elle tamir kaydı ----

    [ObservableProperty] private string _newRepairDescription = string.Empty;
    [ObservableProperty] private string _newRepairMileage = string.Empty;

    // ---- Mesaj satırı ----

    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    // ================= Yükleme =================

    /// <summary>Ekran ilk açılırken müşteri listesini doldurur.</summary>
    public Task InitializeAsync() => LoadCustomersAsync();

    partial void OnSearchTextChanged(string value) => _ = LoadCustomersAsync();

    private async Task LoadCustomersAsync(int? selectId = null)
    {
        var keep = selectId ?? SelectedCustomer?.Id;

        _loadingCustomers = true;
        try
        {
            var list = await _workshop.GetCustomersAsync(SearchText);
            Customers.Clear();
            foreach (var c in list)
                Customers.Add(c);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _loadingCustomers = false;
        }

        SelectedCustomer = keep is { } id ? Customers.FirstOrDefault(c => c.Id == id) : null;
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (_loadingCustomers) return;

        CustomerName = value?.FullName ?? string.Empty;
        CustomerPhone = value?.Phone ?? string.Empty;
        CustomerEmail = value?.Email ?? string.Empty;
        CustomerAddress = value?.Address ?? string.Empty;
        CustomerNotes = value?.Notes ?? string.Empty;

        _ = LoadVehiclesAsync(value?.Id);
    }

    private async Task LoadVehiclesAsync(int? customerId, int? selectVehicleId = null)
    {
        _loadingVehicles = true;
        try
        {
            Vehicles.Clear();
            if (customerId is { } cid)
            {
                foreach (var v in await _workshop.GetVehiclesAsync(cid))
                    Vehicles.Add(v);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            _loadingVehicles = false;
        }

        SelectedVehicle = selectVehicleId is { } vid ? Vehicles.FirstOrDefault(v => v.Id == vid) : null;
        if (SelectedVehicle is null)
        {
            ClearVehicleForm();
            if (_pendingVin is not null)
                VehicleVin = _pendingVin;
            History.Clear();
        }
    }

    partial void OnSelectedVehicleChanged(Vehicle? value)
    {
        if (_loadingVehicles) return;

        if (value is null)
        {
            ClearVehicleForm();
            History.Clear();
            return;
        }

        _pendingVin = null; // kayıtlı bir araç seçildi, bekleyen VIN artık geçerli değil
        VehicleVin = value.Vin;
        VehiclePlate = value.Plate ?? string.Empty;
        VehicleMake = value.Make ?? string.Empty;
        VehicleModel = value.Model ?? string.Empty;
        VehicleYear = value.Year?.ToString() ?? string.Empty;
        VehicleNotes = value.Notes ?? string.Empty;

        _ = LoadHistoryAsync(value.Id);
    }

    private async Task LoadHistoryAsync(int vehicleId)
    {
        try
        {
            var records = await _workshop.GetRepairHistoryAsync(vehicleId);
            // Bu sırada başka bir araç seçilmişse eski cevabı gösterme.
            if (SelectedVehicle?.Id != vehicleId) return;

            History.Clear();
            foreach (var r in records)
                History.Add(RepairHistoryItem.From(r));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Başka yerden (canlı veri ekranı) tamir kaydı eklenince, açıksa geçmiş listesini yeniler.</summary>
    public async Task RefreshHistoryAsync(int vehicleId)
    {
        if (SelectedVehicle?.Id == vehicleId)
            await LoadHistoryAsync(vehicleId);
    }

    /// <summary>
    /// Canlı veri ekranında okunan ama kayıtlı olmayan VIN için araç formunu hazırlar;
    /// kullanıcı bir müşteri seçip "Aracı Kaydet"e basar.
    /// </summary>
    public void PrepareNewVehicle(string vin)
    {
        SelectedVehicle = null;
        ClearVehicleForm();
        _pendingVin = vin;
        VehicleVin = vin;
        SetMessage(SelectedCustomer is null
            ? "Bu şasi numarası için önce soldan bir müşteri seçin (veya yeni müşteri kaydedin), sonra \"Aracı Kaydet\"e basın."
            : $"{SelectedCustomer.FullName} için araç bilgilerini tamamlayıp \"Aracı Kaydet\"e basın.");
    }

    // ================= Müşteri komutları =================

    [RelayCommand]
    private void NewCustomer()
    {
        SelectedCustomer = null;
        CustomerName = CustomerPhone = CustomerEmail = CustomerAddress = CustomerNotes = string.Empty;
        SetMessage("Yeni müşterinin bilgilerini girip \"Müşteriyi Kaydet\"e basın.");
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        var customer = new Customer
        {
            Id = SelectedCustomer?.Id ?? 0,
            FullName = CustomerName,
            Phone = CustomerPhone,
            Email = CustomerEmail,
            Address = CustomerAddress,
            Notes = CustomerNotes
        };

        try
        {
            var saved = await _workshop.SaveCustomerAsync(customer);
            await LoadCustomersAsync(saved.Id);
            SetMessage("Müşteri kaydedildi.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private bool HasCustomer() => SelectedCustomer is not null;

    [RelayCommand(CanExecute = nameof(HasCustomer))]
    private async Task DeleteCustomerAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null) return;

        if (!_dialogs.Confirm("Müşteriyi Sil", $"\"{customer.FullName}\" müşterisi silinecek. Devam etmek istiyor musunuz?"))
            return;

        try
        {
            await _workshop.DeleteCustomerAsync(customer.Id);
            SelectedCustomer = null; // formu ve araç listesini temizler
            await LoadCustomersAsync();
            SetMessage("Müşteri silindi.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ================= Araç komutları =================

    [RelayCommand]
    private void NewVehicle()
    {
        _pendingVin = null;
        SelectedVehicle = null;
        ClearVehicleForm();
        SetMessage("Yeni aracın bilgilerini girip \"Aracı Kaydet\"e basın.");
    }

    [RelayCommand(CanExecute = nameof(HasCustomer))]
    private async Task SaveVehicleAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null) return;

        int? year = null;
        if (!string.IsNullOrWhiteSpace(VehicleYear))
        {
            if (!int.TryParse(VehicleYear.Trim(), out var y))
            {
                SetMessage("Model yılı sadece rakamlardan oluşmalı (örn. 2005).", isError: true);
                return;
            }
            year = y;
        }

        var vehicle = new Vehicle
        {
            Id = SelectedVehicle?.Id ?? 0,
            CustomerId = customer.Id,
            Vin = VehicleVin,
            Plate = VehiclePlate,
            Make = VehicleMake,
            Model = VehicleModel,
            Year = year,
            Notes = VehicleNotes
        };

        try
        {
            var saved = await _workshop.SaveVehicleAsync(vehicle);
            _pendingVin = null;
            await LoadVehiclesAsync(customer.Id, saved.Id);
            SetMessage("Araç kaydedildi.");
            VehicleSaved?.Invoke(this, saved);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private bool HasVehicle() => SelectedVehicle is not null;

    [RelayCommand(CanExecute = nameof(HasVehicle))]
    private async Task DeleteVehicleAsync()
    {
        var vehicle = SelectedVehicle;
        var customer = SelectedCustomer;
        if (vehicle is null || customer is null) return;

        var title = string.IsNullOrWhiteSpace(vehicle.Plate) ? vehicle.Vin : vehicle.Plate;
        if (!_dialogs.Confirm("Aracı Sil", $"\"{title}\" aracı silinecek. Devam etmek istiyor musunuz?"))
            return;

        try
        {
            await _workshop.DeleteVehicleAsync(vehicle.Id);
            await LoadVehiclesAsync(customer.Id);
            SetMessage("Araç silindi.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ================= Tamir kaydı (elle) =================

    [RelayCommand(CanExecute = nameof(HasVehicle))]
    private async Task AddRepairAsync()
    {
        var vehicle = SelectedVehicle;
        if (vehicle is null) return;

        int? mileage = null;
        if (!string.IsNullOrWhiteSpace(NewRepairMileage))
        {
            if (!int.TryParse(NewRepairMileage.Replace(".", "").Replace(" ", ""), out var km))
            {
                SetMessage("Kilometre sadece rakamlardan oluşmalı.", isError: true);
                return;
            }
            mileage = km;
        }

        try
        {
            await _workshop.AddRepairAsync(new RepairRecord
            {
                VehicleId = vehicle.Id,
                Date = DateTime.Now,
                Mileage = mileage,
                Description = NewRepairDescription
            });

            NewRepairDescription = string.Empty;
            NewRepairMileage = string.Empty;
            await LoadHistoryAsync(vehicle.Id);
            SetMessage("Tamir kaydı eklendi.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ================= Yardımcılar =================

    private void ClearVehicleForm() =>
        VehicleVin = VehiclePlate = VehicleMake = VehicleModel = VehicleYear = VehicleNotes = string.Empty;

    private void SetMessage(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
    }

    private void ShowError(Exception ex) => SetMessage(
        ex is DataValidationException ? ex.Message : $"Veritabanında bir sorun oluştu: {ex.Message}",
        isError: true);
}
