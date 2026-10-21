using System.IO;
using FluentAssertions;
using ObdDiag.App.Services;

namespace ObdDiag.App.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ObdDiagTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Missing_File_Gives_Empty_Settings()
    {
        var settings = new JsonSettingsStore(_folder).Load();

        settings.Name.Should().BeEmpty();
        settings.Address.Should().BeNull();
        settings.Logo.Should().BeNull();
    }

    [Fact]
    public void Saved_Settings_Are_Read_Back_Including_Turkish_Text_And_Logo()
    {
        var logo = new byte[] { 1, 2, 3, 4 };
        new JsonSettingsStore(_folder).Save(new WorkshopSettings
        {
            Name = "Kaya Oto Elektrik",
            Address = "Sanayi Sitesi, Elazığ",
            Phone = "0424 123 45 67",
            Email = "info@kayaoto.com",
            Logo = logo
        });

        var loaded = new JsonSettingsStore(_folder).Load(); // yeni nesne: gerçekten diskten okunsun

        loaded.Name.Should().Be("Kaya Oto Elektrik");
        loaded.Address.Should().Be("Sanayi Sitesi, Elazığ");
        loaded.Phone.Should().Be("0424 123 45 67");
        loaded.Email.Should().Be("info@kayaoto.com");
        loaded.Logo.Should().Equal(logo);
    }

    [Fact]
    public void Removing_Logo_Deletes_The_Logo_File()
    {
        var store = new JsonSettingsStore(_folder);
        store.Save(new WorkshopSettings { Name = "Servis", Logo = new byte[] { 9, 9 } });

        store.Save(new WorkshopSettings { Name = "Servis", Logo = null });

        store.Load().Logo.Should().BeNull();
        File.Exists(Path.Combine(_folder, "logo.img")).Should().BeFalse();
    }

    [Fact]
    public void Corrupt_Settings_File_Does_Not_Crash()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ bu json değil");

        var settings = new JsonSettingsStore(_folder).Load();

        settings.Name.Should().BeEmpty();
    }
}
