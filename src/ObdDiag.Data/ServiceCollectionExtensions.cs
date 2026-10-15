using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ObdDiag.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>Veritabanı ve kayıt servisini uygulamaya tanıtır. <paramref name="databasePath"/> .db dosyasının yoludur.</summary>
    public static IServiceCollection AddObdData(this IServiceCollection services, string databasePath)
    {
        services.AddDbContextFactory<ObdDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<IWorkshopService, WorkshopService>();
        return services;
    }

    /// <summary>Veritabanı dosyası ve tabloları yoksa oluşturur. Uygulama açılırken bir kez çağrılır.</summary>
    public static void EnsureObdDatabaseCreated(this IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ObdDbContext>>();
        using var db = factory.CreateDbContext();

        var path = db.Database.GetDbConnection().DataSource;
        var directory = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        db.Database.EnsureCreated();
    }
}
