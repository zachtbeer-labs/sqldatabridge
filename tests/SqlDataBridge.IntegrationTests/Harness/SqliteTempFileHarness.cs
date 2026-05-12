using Microsoft.Data.Sqlite;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

internal sealed class SqliteTempFileHarness : IAsyncDisposable
{
    public string FilePath { get; }

    public SqliteTempFileHarness()
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"zsb-{Guid.NewGuid():N}.sqlite");
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection($"Data Source={FilePath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        return ValueTask.CompletedTask;
    }
}
