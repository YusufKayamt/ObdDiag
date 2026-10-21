using System.IO;
using System.Text;
using FluentAssertions;
using Moq;
using ObdDiag.App.Services;
using ObdDiag.App.ViewModels;
using ObdDiag.Reporting;

namespace ObdDiag.App.Tests;

public class SettingsViewModelTests : IDisposable
{
    /// <summary>1x1 piksellik geçerli PNG.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ObdDiagTests_" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ISettingsStore> _store = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private WorkshopSettings? _saved;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _store.Setup(s => s.Load()).Returns(new WorkshopSettings { Name = "Kayıtlı Servis", Phone = "0424 000 00 00" });
        _store.Setup(s => s.Save(It.IsAny<WorkshopSettings>())).Callback<WorkshopSettings>(s => _saved = s);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SettingsViewModel Create() =>
        new(_store.Object, new PdfReportGenerator(), _dialogs.Object) { PreviewPath = Path.Combine(_folder, "ornek.pdf") };

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Saved_Settings_Are_Shown_On_Open()
    {
        var vm = Create();

        vm.WorkshopName.Should().Be("Kayıtlı Servis");
        vm.Phone.Should().Be("0424 000 00 00");
        vm.Address.Should().BeEmpty();
        vm.HasLogo.Should().BeFalse();
        vm.RemoveLogoCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Save_Stores_Trimmed_Values()
    {
        var vm = Create();
        vm.WorkshopName = "  Kaya Oto  ";
        vm.Address = "  Elazığ ";
        vm.Email = "";

        vm.SaveCommand.Execute(null);

        _saved.Should().NotBeNull();
        _saved!.Name.Should().Be("Kaya Oto");
        _saved.Address.Should().Be("Elazığ");
        _saved.Email.Should().BeNull();
        vm.IsError.Should().BeFalse();
    }

    [Fact]
    public void Empty_Name_Is_Not_Saved()
    {
        var vm = Create();
        vm.WorkshopName = "   ";

        vm.SaveCommand.Execute(null);

        _saved.Should().BeNull();
        vm.IsError.Should().BeTrue();
        vm.Message.Should().Contain("İşletme adı");
    }

    [Fact]
    public void Invalid_Email_Is_Not_Saved()
    {
        var vm = Create();
        vm.Email = "kayaoto.com";

        vm.SaveCommand.Execute(null);

        _saved.Should().BeNull();
        vm.IsError.Should().BeTrue();
    }

    [Fact]
    public void Valid_Logo_Is_Chosen_And_Saved()
    {
        var path = WriteFile("logo.png", TinyPng);
        _dialogs.Setup(d => d.AskOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(path);
        var vm = Create();

        vm.ChooseLogoCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.HasLogo.Should().BeTrue();
        vm.RemoveLogoCommand.CanExecute(null).Should().BeTrue();
        _saved!.Logo.Should().Equal(TinyPng);
    }

    [Fact]
    public void File_That_Is_Not_An_Image_Is_Rejected()
    {
        var path = WriteFile("logo.png", Encoding.ASCII.GetBytes("resim degil"));
        _dialogs.Setup(d => d.AskOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(path);
        var vm = Create();

        vm.ChooseLogoCommand.Execute(null);

        vm.HasLogo.Should().BeFalse();
        vm.IsError.Should().BeTrue();
    }

    [Fact]
    public void Too_Big_Logo_Is_Rejected()
    {
        var path = WriteFile("buyuk.png", new byte[SettingsViewModel.MaxLogoBytes + 1]);
        _dialogs.Setup(d => d.AskOpenFile(It.IsAny<string>(), It.IsAny<string>())).Returns(path);
        var vm = Create();

        vm.ChooseLogoCommand.Execute(null);

        vm.HasLogo.Should().BeFalse();
        vm.Message.Should().Contain("5 MB");
    }

    [Fact]
    public void Cancelled_Logo_Dialog_Changes_Nothing()
    {
        var vm = Create();

        vm.ChooseLogoCommand.Execute(null); // AskOpenFile null döner (vazgeçildi)

        vm.HasLogo.Should().BeFalse();
        vm.Message.Should().BeEmpty();
    }

    [Fact]
    public void Logo_Can_Be_Removed()
    {
        _store.Setup(s => s.Load()).Returns(new WorkshopSettings { Name = "Servis", Logo = TinyPng });
        var vm = Create();
        vm.HasLogo.Should().BeTrue();

        vm.RemoveLogoCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.HasLogo.Should().BeFalse();
        _saved!.Logo.Should().BeNull();
    }

    [Fact]
    public async Task Preview_Writes_Pdf_And_Opens_It()
    {
        var vm = Create();

        await vm.PreviewCommand.ExecuteAsync(null);

        File.Exists(vm.PreviewPath).Should().BeTrue();
        Encoding.ASCII.GetString(File.ReadAllBytes(vm.PreviewPath), 0, 5).Should().Be("%PDF-");
        _dialogs.Verify(d => d.OpenFile(vm.PreviewPath), Times.Once);
        _saved.Should().BeNull(); // önizleme ayarları kaydetmez
    }
}
