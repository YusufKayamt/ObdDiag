using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace ObdDiag.App.Services;

public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public string? AskSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? AskOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // PDF açacak program yoksa dosya yine de kaydedilmiştir; sadece haber veriyoruz.
            MessageBox.Show($"Dosya kaydedildi ama açılamadı:\n{path}\n\n{ex.Message}",
                "Dosya Açılamadı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
