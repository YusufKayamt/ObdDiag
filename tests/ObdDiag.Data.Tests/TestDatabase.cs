using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ObdDiag.Data;

namespace ObdDiag.Data.Tests;

/// <summary>
/// Her test için bellekte geçici bir SQLite veritabanı. Bağlantı açık tutulduğu sürece veriler yaşar;
/// test bitince <see cref="Dispose"/> ile hepsi silinir.
/// </summary>
public sealed class TestDatabase : IDbContextFactory<ObdDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ObdDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
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
