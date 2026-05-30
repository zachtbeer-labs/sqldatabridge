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

    /// <summary>
    /// Copies a local <c>.bak</c> into the container and restores it into a freshly-named database, returning the
    /// per-database connection string. The logical-file moves are derived from <c>RESTORE FILELISTONLY</c> so the
    /// caller does not need to know the backup's internal file layout; in particular a memory-optimized /
    /// FILESTREAM filegroup (<c>Type = 'S'</c>) is moved to a directory rather than a file. Used by the opt-in
    /// WideWorldImporters tests; the restored database is read-only from the test's perspective.
    /// </summary>
    public async Task<string> RestoreDatabaseFromBakAsync(string localBakPath, CancellationToken cancellationToken = default)
    {
        var databaseName = $"zsb_wwi_{Guid.NewGuid():N}";
        var containerBakPath = $"/var/opt/mssql/data/{Guid.NewGuid():N}.bak";

        var bakBytes = await File.ReadAllBytesAsync(localBakPath, cancellationToken);
        await _container.CopyAsync(bakBytes, containerBakPath);

        await using var master = new SqlConnection(MasterConnectionString);
        await master.OpenAsync(cancellationToken);

        var moves = new List<string>();
        await using (var fileList = master.CreateCommand())
        {
            fileList.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{containerBakPath}'";
            fileList.CommandTimeout = 600;
            await using var reader = await fileList.ExecuteReaderAsync(cancellationToken);
            var logicalNameOrdinal = reader.GetOrdinal("LogicalName");
            var typeOrdinal = reader.GetOrdinal("Type");
            var index = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var logicalName = reader.GetString(logicalNameOrdinal);
                var fileType = reader.GetString(typeOrdinal); // 'D' = data, 'L' = log, 'S' = in-memory/FILESTREAM container.
                var isMemoryOptimized = fileType.Equals("S", StringComparison.OrdinalIgnoreCase);
                var extension = isMemoryOptimized
                    ? string.Empty // 'S' is restored to a directory, not a file.
                    : fileType.Equals("L", StringComparison.OrdinalIgnoreCase) ? ".ldf" : ".mdf";
                var targetPath = $"/var/opt/mssql/data/{databaseName}_{index}{extension}";
                moves.Add($"MOVE N'{logicalName.Replace("'", "''")}' TO N'{targetPath}'");
                index++;
            }
        }

        await using (var restore = master.CreateCommand())
        {
            restore.CommandText =
                $"RESTORE DATABASE [{databaseName}] FROM DISK = N'{containerBakPath}' WITH {string.Join(", ", moves)}, REPLACE, RECOVERY";
            restore.CommandTimeout = 600;
            await restore.ExecuteNonQueryAsync(cancellationToken);
        }

        return new SqlConnectionStringBuilder(MasterConnectionString) { InitialCatalog = databaseName }.ConnectionString;
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
