using Moq;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Reporting;

namespace ObdDiag.App.Tests;

/// <summary>Testlerde ViewModel kurarken tekrar eden parçalar.</summary>
internal static class TestViewModels
{
    /// <summary>Boş ayarlarla çalışan, diske yazmayan Ayarlar ekranı.</summary>
    public static SettingsViewModel Settings(IDialogService dialogs)
    {
        var store = new Mock<ISettingsStore>();
        store.Setup(s => s.Load()).Returns(new WorkshopSettings());
        return new SettingsViewModel(store.Object, new PdfReportGenerator(), dialogs);
    }
}
