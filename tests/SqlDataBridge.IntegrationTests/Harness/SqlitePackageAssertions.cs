using Microsoft.Data.Sqlite;
using Shouldly;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

internal static class SqlitePackageAssertions
{
    private static readonly string[] RequiredMetadataTables =
    [
        "zsb_export_runs",
        "zsb_tables",
        "zsb_columns",
        "zsb_exclusions",
        "zsb_warnings",
        "zsb_table_stats",
        "zsb_import_plan"
    ];

    public static async Task HasRequiredMetadataTablesAsync(SqliteConnection connection)
    {
        foreach (var table in RequiredMetadataTables)
        {
            (await connection.TableExistsAsync(table)).ShouldBeTrue($"Expected metadata table '{table}' to exist.");
        }
    }

    public static async Task HasExportedTablesAsync(SqliteConnection connection, params string[] expectedFullNames)
    {
        var actual = await connection.ReadStringsAsync("""
            SELECT source_schema || '.' || source_table
            FROM zsb_tables
            ORDER BY source_schema, source_table
            """);

        actual.ShouldBe(expectedFullNames.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task HasImportPlanAsync(SqliteConnection connection, params string[] expectedFullNames)
    {
        var actual = await connection.ReadStringsAsync("""
            SELECT source_schema || '.' || source_table
            FROM zsb_import_plan
            ORDER BY sequence
            """);

        actual.ShouldBe(expectedFullNames);
    }

    public static async Task HasRunMetadataAsync(SqliteConnection connection)
    {
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_export_runs
            WHERE package_format_version = 4
              AND application_version <> ''
              AND exported_at_utc <> ''
              AND length(source_schema_hash) = 64
            """)).ShouldBe(1);
    }

    public static async Task HasTableRowCountAsync(SqliteConnection connection, string fullName, int expectedRows)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.exported_row_count, s.estimated_source_row_count, s.estimated_source_bytes, s.export_batch_size
            FROM zsb_table_stats s
            INNER JOIN zsb_tables t ON t.id = s.table_id
            WHERE t.source_schema || '.' || t.source_table = $name
            """;
        command.Parameters.AddWithValue("$name", fullName);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue($"Expected table stats for '{fullName}'.");
        reader.GetInt32(0).ShouldBe(expectedRows);
        reader.GetInt64(1).ShouldBeGreaterThanOrEqualTo(0);
        reader.GetInt64(2).ShouldBeGreaterThanOrEqualTo(0);
        reader.GetInt32(3).ShouldBeGreaterThan(0);
    }

    public static async Task HasExclusionAsync(SqliteConnection connection, string type, string targetName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM zsb_exclusions
            WHERE exclusion_type = $type
              AND target_name = $target
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$target", targetName);
        Convert.ToInt32((await command.ExecuteScalarAsync())).ShouldBe(1);
    }
}
