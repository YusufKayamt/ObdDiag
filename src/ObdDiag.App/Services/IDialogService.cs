namespace ObdDiag.App.Services;

/// <summary>Kullanıcıya soru soran pencereler. ViewModel doğrudan MessageBox kullanmasın (test edilebilsin) diye ayrıldı.</summary>
public interface IDialogService
{
    /// <summary>"Evet / Hayır" sorusu sorar. Evet ise true döner.</summary>
    bool Confirm(string title, string message);

    /// <summary>"Farklı kaydet" penceresi açar. Seçilen dosya yolunu, vazgeçilirse null döner.</summary>
    /// <param name="filter">Dosya türü süzgeci, örn. "PDF dosyası (*.pdf)|*.pdf"</param>
    string? AskSaveFile(string title, string defaultFileName, string filter);

    /// <summary>"Dosya aç" penceresi açar. Seçilen dosya yolunu, vazgeçilirse null döner.</summary>
    string? AskOpenFile(string title, string filter);

    /// <summary>Dosyayı Windows'taki varsayılan programla açar (PDF için örn. Edge veya Acrobat).</summary>
    void OpenFile(string path);
}
