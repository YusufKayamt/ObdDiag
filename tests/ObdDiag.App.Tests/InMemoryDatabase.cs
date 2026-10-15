using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ObdDiag.Data;

namespace ObdDiag.App.Tests;

/// <summary>Testlerde gerçek veritabanı katmanını kullanmak için bellekte geçici SQLite.</summary>
public sealed class InMemoryDatabase : IDbContextFactory<ObdDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ObdDbContext> _options;

    public InMemoryDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<ObdDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
        Service = new WorkshopService(this);
    }

    public IWorkshopService Service { get; }

    public ObdDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
