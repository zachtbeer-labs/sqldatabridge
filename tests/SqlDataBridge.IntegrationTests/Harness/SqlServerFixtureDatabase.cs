using Microsoft.Data.SqlClient;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

internal sealed class SqlServerFixtureDatabase : IAsyncDisposable
{
    private readonly string _masterConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    public SqlServerFixtureDatabase(string masterConnectionString, string databaseName, string connectionString)
    {
        _masterConnectionString = masterConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public static async Task<SqlServerFixtureDatabase> CreateAsync(SqlServerContainerFixture fixture)
    {
        var databaseName = $"zsb_{Guid.NewGuid():N}";
        var connectionString = await fixture.CreateDatabaseAsync(databaseName);
        return new SqlServerFixtureDatabase(fixture.MasterConnectionString, databaseName, connectionString);
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ScalarIntAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<string> ScalarStringAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result)!;
    }

    public async ValueTask DisposeAsync()
    {
        await using var master = new SqlConnection(_masterConnectionString);
        await master.OpenAsync();

        await using (var singleUser = master.CreateCommand())
        {
            singleUser.CommandText = $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
            singleUser.CommandTimeout = 120;
            await singleUser.ExecuteNonQueryAsync();
        }

        await using (var drop = master.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE [{DatabaseName}]";
            drop.CommandTimeout = 120;
            await drop.ExecuteNonQueryAsync();
        }
    }
}
