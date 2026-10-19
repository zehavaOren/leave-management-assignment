using LeaveManagement.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace LeaveManagement.Tests;

// The tests run against a real, throwaway PostgreSQL started by Testcontainers,
// which needs Docker on the machine.
//
// No Docker? Set the environment variable Database__Provider=Sqlite and the very
// same tests run against an in-memory SQLite instead:
//
//     Database__Provider=Sqlite dotnet test          (bash)
//     $env:Database__Provider="Sqlite"; dotnet test  (PowerShell)
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly List<SqliteConnection> _sqliteConnections = new();
    private PostgreSqlContainer? _postgres;

    public static bool UseSqlite => string.Equals(
        Environment.GetEnvironmentVariable("Database__Provider"),
        "Sqlite",
        StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (UseSqlite) return;

        _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("leave_tests").Build();
        await _postgres.StartAsync();
    }

    // Gives each test its own empty database.
    public LeaveDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>();

        if (UseSqlite)
        {
            // The in-memory database lives as long as its connection, so keep it open.
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            _sqliteConnections.Add(connection);
            options.UseSqlite(connection);
        }
        else
        {
            options.UseNpgsql(_postgres!.GetConnectionString());
        }

        var db = new LeaveDbContext(options.Options);
        if (!UseSqlite) db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        return db;
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _sqliteConnections) connection.Dispose();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}
