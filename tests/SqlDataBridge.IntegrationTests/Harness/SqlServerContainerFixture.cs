using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public const string SqlServerImageEnvironmentVariable = "SQLDATABRIDGE_SQLSERVER_IMAGE";
    public const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2025-latest";

    private readonly MsSqlContainer _container;

    public SqlServerContainerFixture()
    {
        var configuredImage = Environment.GetEnvironmentVariable(SqlServerImageEnvironmentVariable);
        ImageName = string.IsNullOrWhiteSpace(configuredImage) ? DefaultSqlServerImage : configuredImage;

        _container = new MsSqlBuilder(ImageName)
            .WithPassword("Your_strong_Password123")
            .Build();
    }

    public string MasterConnectionString => _container.GetConnectionString();
    public string ImageName { get; }
    public int? ServerMajorVersion { get; private set; }
    public bool SupportsNativeJson => ServerMajorVersion >= 17 || ImageName.Contains(":2025", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ServerMajorVersion = await ReadServerMajorVersionAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<string> CreateDatabaseAsync(string? databaseName = null)
    {
        databaseName ??= $"zsb_{Guid.NewGuid():N}";

        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();

        await using (var create = master.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            create.CommandTimeout = 120;
            await create.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    private async Task<int> ReadServerMajorVersionAsync()
    {
        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync();

        await using var command = master.CreateCommand();
        command.CommandText = "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'))";
        command.CommandTimeout = 120;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
