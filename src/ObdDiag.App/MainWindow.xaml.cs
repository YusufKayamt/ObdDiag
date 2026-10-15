using System.ComponentModel;
using System.Windows;
using ObdDiag.App.ViewModels;

namespace ObdDiag.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Pencere açılınca müşteri listesini veritabanından yükle.
        Loaded += async (_, _) => await _viewModel.Customers.InitializeAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Pencere kapanırken cihazı düzgünce serbest bırak.
        _viewModel.Shutdown();
        base.OnClosing(e);
    }
}
