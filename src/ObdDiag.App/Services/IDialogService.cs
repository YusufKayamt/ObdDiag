namespace ObdDiag.App.Services;

/// <summary>Kullanıcıya soru soran pencereler. ViewModel doğrudan MessageBox kullanmasın (test edilebilsin) diye ayrıldı.</summary>
public interface IDialogService
{
    /// <summary>"Evet / Hayır" sorusu sorar. Evet ise true döner.</summary>
    bool Confirm(string title, string message);
}
