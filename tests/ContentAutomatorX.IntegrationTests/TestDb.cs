using ContentAutomatorX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public sealed class TestDb : IDisposable
{
    public AppDbContext Db { get; }
    private readonly string _path;

    private TestDb(AppDbContext db, string path) { Db = db; _path = path; }

    public static TestDb Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"contentx-test-{Guid.NewGuid():N}.db");
        var db = NewContext(path);
        db.Database.Migrate();
        return new TestDb(db, path);
    }

    public AppDbContext NewContext() => NewContext(_path);

    private static AppDbContext NewContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    public void Dispose()
    {
        Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { /* best effort */ }
    }
}
