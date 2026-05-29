using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.Tests;

// Covers the SchemaPackage <-> zsb_schema_packages round-trip across the new source_engine_edition
// column (added in package format v4). The column is the source-platform stamp consumed by
// DacpacSchemaManager.ShouldAdaptAzureSourceForOnPremTarget, so silently losing it on write or read
// would re-introduce the Msg 12824 regression on cross-platform deploys.
public sealed class SqlitePackageSchemaPackageRoundTripTests
{
    [Theory]
    [InlineData(5)]   // Azure SQL Database
    [InlineData(8)]   // Azure SQL Managed Instance
    [InlineData(3)]   // On-prem Enterprise
    [InlineData(null)]
    public async Task StoreSchemaPackage_RoundTripsSourceEngineEdition(int? sourceEngineEdition)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await CreateSchemaPackagesTableAsync(connection);

        var payload = new byte[] { 1, 2, 3, 4 };
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        var written = new SchemaPackage(
            "dacpac",
            "name.dacpac",
            sha,
            DateTimeOffset.UtcNow,
            "SourceDb",
            "170.0.0",
            DacpacSchemaScope.Database,
            payload,
            sourceEngineEdition);

        await SqlitePackage.StoreSchemaPackageAsync(connection, written, CancellationToken.None);

        var read = await SqlitePackage.ReadSchemaPackageAsync(connection, CancellationToken.None);

        read.ShouldNotBeNull();
        read.SourceEngineEdition.ShouldBe(sourceEngineEdition);
        read.PackageType.ShouldBe(written.PackageType);
        read.PackageSha256.ShouldBe(written.PackageSha256);
        read.SchemaScope.ShouldBe(written.SchemaScope);
        read.Payload.ShouldBe(written.Payload);
    }

    private static async Task CreateSchemaPackagesTableAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE zsb_schema_packages (
                id INTEGER PRIMARY KEY,
                package_type TEXT NOT NULL,
                package_name TEXT NOT NULL,
                package_sha256 TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                source_database_name TEXT NULL,
                dacfx_version TEXT NULL,
                schema_scope TEXT NULL,
                payload BLOB NOT NULL,
                source_engine_edition INTEGER NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }
}
