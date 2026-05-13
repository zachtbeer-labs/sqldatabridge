using Microsoft.Data.Sqlite;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class SqlitePackageValidationTests
{
    [Fact]
    public async Task ValidateForImportAsync_MissingMetadataTable_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("required metadata table 'zsb_export_runs' is missing");
    }

    [Fact]
    public async Task ValidateForImportAsync_NoTableMetadata_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("no table metadata exists");
    }

    [Fact]
    public async Task ValidateForImportAsync_MissingPackageFormatVersion_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateLegacyMetadataTablesAsync(connection);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("package format version metadata is missing");
    }

    [Fact]
    public async Task ValidateForImportAsync_UnsupportedPackageFormatVersion_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, "UPDATE zsb_export_runs SET package_format_version = 4;");

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("format version '4' is not supported");
    }

    [Fact]
    public async Task ValidateForImportAsync_EmptyImportPlan_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO zsb_tables(id, source_schema, source_table, sqlite_table)
            VALUES (1, 'dbo', 'Customers', 'zsb_data_dbo__customers');
            """);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("import plan metadata is empty");
    }

    [Fact]
    public async Task ValidateForImportAsync_ImportPlanReferencesMissingTableMetadata_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO zsb_tables(id, source_schema, source_table, sqlite_table)
            VALUES (1, 'dbo', 'Customers', 'zsb_data_dbo__customers');

            INSERT INTO zsb_import_plan(sequence, source_schema, source_table)
            VALUES (0, 'dbo', 'Orders');
            """);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("import plan references table 'dbo.Orders'");
    }

    [Fact]
    public async Task ValidateForImportAsync_MissingRowCountMetadata_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO zsb_tables(id, source_schema, source_table, sqlite_table)
            VALUES (1, 'dbo', 'Customers', 'zsb_data_dbo__customers');

            INSERT INTO zsb_import_plan(sequence, source_schema, source_table)
            VALUES (0, 'dbo', 'Customers');

            CREATE TABLE zsb_data_dbo__customers (Id INTEGER);
            """);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("row-count metadata is missing for 'dbo.Customers'");
    }

    [Fact]
    public async Task ValidateForImportAsync_MissingDataTable_ThrowsBridgeException()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO zsb_tables(id, source_schema, source_table, sqlite_table)
            VALUES (1, 'dbo', 'Customers', 'zsb_data_dbo__customers');

            INSERT INTO zsb_import_plan(sequence, source_schema, source_table)
            VALUES (0, 'dbo', 'Customers');

            INSERT INTO zsb_table_stats(table_id, exported_row_count, estimated_source_row_count, estimated_source_bytes, export_batch_size)
            VALUES (1, 0, 0, 0, 1000);
            """);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None));

        exception.Message.ShouldContain("data table 'zsb_data_dbo__customers'");
    }

    [Fact]
    public async Task ValidateForImportAsync_ValidMinimalPackage_Succeeds()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateMetadataTablesAsync(connection);
        await ExecuteAsync(connection, """
            INSERT INTO zsb_tables(id, source_schema, source_table, sqlite_table)
            VALUES (1, 'dbo', 'Customers', 'zsb_data_dbo__customers');

            INSERT INTO zsb_import_plan(sequence, source_schema, source_table)
            VALUES (0, 'dbo', 'Customers');

            INSERT INTO zsb_table_stats(table_id, exported_row_count, estimated_source_row_count, estimated_source_bytes, export_batch_size)
            VALUES (1, 0, 0, 0, 1000);

            CREATE TABLE zsb_data_dbo__customers (Id INTEGER);
            """);

        await SqlitePackage.ValidateForImportAsync(connection, CancellationToken.None);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static Task CreateMetadataTablesAsync(SqliteConnection connection)
    {
        return ExecuteAsync(connection, """
            CREATE TABLE zsb_export_runs (
                id INTEGER PRIMARY KEY,
                package_format_version INTEGER NOT NULL,
                application_version TEXT NOT NULL,
                exported_at_utc TEXT NOT NULL,
                source_schema_hash TEXT NOT NULL
            );
            CREATE TABLE zsb_tables (
                id INTEGER PRIMARY KEY,
                source_schema TEXT NOT NULL,
                source_table TEXT NOT NULL,
                sqlite_table TEXT NOT NULL
            );
            CREATE TABLE zsb_columns (
                table_id INTEGER NOT NULL,
                column_name TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                sql_server_type_name TEXT NOT NULL,
                max_length INTEGER NOT NULL,
                precision_value INTEGER NOT NULL,
                scale_value INTEGER NOT NULL,
                is_nullable INTEGER NOT NULL,
                is_identity INTEGER NOT NULL,
                is_computed INTEGER NOT NULL,
                is_excluded INTEGER NOT NULL,
                collation_name TEXT NULL
            );
            CREATE TABLE zsb_exclusions (
                exclusion_type TEXT NOT NULL,
                target_name TEXT NOT NULL
            );
            CREATE TABLE zsb_warnings (
                warning_text TEXT NOT NULL
            );
            CREATE TABLE zsb_table_stats (
                table_id INTEGER NOT NULL,
                exported_row_count INTEGER NOT NULL,
                estimated_source_row_count INTEGER NOT NULL,
                estimated_source_bytes INTEGER NOT NULL,
                export_batch_size INTEGER NOT NULL
            );
            CREATE TABLE zsb_import_plan (
                sequence INTEGER NOT NULL,
                source_schema TEXT NOT NULL,
                source_table TEXT NOT NULL
            );
            CREATE TABLE zsb_schema_packages (
                id INTEGER PRIMARY KEY,
                package_type TEXT NOT NULL,
                package_name TEXT NOT NULL,
                package_sha256 TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                source_database_name TEXT NULL,
                dacfx_version TEXT NULL,
                schema_scope TEXT NULL,
                payload BLOB NOT NULL
            );

            INSERT INTO zsb_export_runs(id, package_format_version, application_version, exported_at_utc, source_schema_hash)
            VALUES (1, 3, '1.0.0', '2026-01-01T00:00:00.0000000Z', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
            """);
    }

    private static Task CreateLegacyMetadataTablesAsync(SqliteConnection connection)
    {
        return ExecuteAsync(connection, """
            CREATE TABLE zsb_export_runs (
                id INTEGER PRIMARY KEY,
                application_version TEXT NOT NULL,
                exported_at_utc TEXT NOT NULL,
                source_schema_hash TEXT NOT NULL
            );
            CREATE TABLE zsb_tables (
                id INTEGER PRIMARY KEY,
                source_schema TEXT NOT NULL,
                source_table TEXT NOT NULL,
                sqlite_table TEXT NOT NULL
            );
            CREATE TABLE zsb_columns (
                table_id INTEGER NOT NULL,
                column_name TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                sql_server_type_name TEXT NOT NULL,
                max_length INTEGER NOT NULL,
                precision_value INTEGER NOT NULL,
                scale_value INTEGER NOT NULL,
                is_nullable INTEGER NOT NULL,
                is_identity INTEGER NOT NULL,
                is_computed INTEGER NOT NULL,
                is_excluded INTEGER NOT NULL,
                collation_name TEXT NULL
            );
            CREATE TABLE zsb_exclusions (
                exclusion_type TEXT NOT NULL,
                target_name TEXT NOT NULL
            );
            CREATE TABLE zsb_warnings (
                warning_text TEXT NOT NULL
            );
            CREATE TABLE zsb_table_stats (
                table_id INTEGER NOT NULL,
                exported_row_count INTEGER NOT NULL,
                estimated_source_row_count INTEGER NOT NULL,
                estimated_source_bytes INTEGER NOT NULL,
                export_batch_size INTEGER NOT NULL
            );
            CREATE TABLE zsb_import_plan (
                sequence INTEGER NOT NULL,
                source_schema TEXT NOT NULL,
                source_table TEXT NOT NULL
            );
            CREATE TABLE zsb_schema_packages (
                id INTEGER PRIMARY KEY,
                package_type TEXT NOT NULL,
                package_name TEXT NOT NULL,
                package_sha256 TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                source_database_name TEXT NULL,
                dacfx_version TEXT NULL,
                payload BLOB NOT NULL
            );
            """);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
