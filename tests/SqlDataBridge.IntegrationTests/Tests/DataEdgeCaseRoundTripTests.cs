using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

// Real-world data shapes that are easy to get wrong: NULL-heavy rows, extended Unicode,
// and datetime/decimal boundary values. Large (>1 MB) VARBINARY(MAX) is intentionally
// excluded — the bulk-copy code path is identical to small varbinary and the runtime
// cost is not worth the marginal coverage.
[Collection(nameof(SqlServerCollection))]
public sealed class DataEdgeCaseRoundTripTests
{
    private readonly SqlServerContainerFixture _fixture;

    public DataEdgeCaseRoundTripTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RoundTrip_NullHeavyTable_PreservesNullPatternAndValues()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("null_heavy.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.SparseRows());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(10);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.SparseRows")).ShouldBe(10);

        foreach (var column in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
        {
            var sourceNulls = await source.ScalarIntAsync($"SELECT COUNT(*) FROM dbo.SparseRows WHERE {column} IS NULL");
            var targetNulls = await target.ScalarIntAsync($"SELECT COUNT(*) FROM dbo.SparseRows WHERE {column} IS NULL");
            targetNulls.ShouldBe(sourceNulls, $"NULL count mismatch for column {column}");
        }

        // Fully-NULL rows (Id 3..8) must survive intact.
        (await target.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.SparseRows WHERE A IS NULL AND B IS NULL AND C IS NULL AND D IS NULL AND E IS NULL AND F IS NULL AND G IS NULL AND H IS NULL"))
            .ShouldBe(6);

        // Spot-check one fully-populated row by binary equality of the binary/guid columns.
        (await target.ScalarStringAsync(
            "SELECT CONVERT(VARCHAR(20), G, 1) FROM dbo.SparseRows WHERE Id = 1")).ShouldBe("0xDEADBEEF");
        (await target.ScalarStringAsync(
            "SELECT CONVERT(VARCHAR(50), H) FROM dbo.SparseRows WHERE Id = 1")).ShouldBe("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task RoundTrip_ExtendedUnicode_PreservesAllCodePoints()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("unicode_extremes.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.UnicodeRows());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(6);

        // Bit-exact comparison: read each side's NVARCHAR as raw UTF-16 bytes and
        // require a 1:1 match. Catches surrogate-pair corruption that a
        // collation-aware string compare could mask.
        var sourceBytes = await ReadLabelBytesAsync(source);
        var targetBytes = await ReadLabelBytesAsync(target);
        targetBytes.ShouldBe(sourceBytes);
    }

    [Fact]
    public async Task RoundTrip_DateAndDecimalExtremes_PreservesBoundaryValues()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("datetime_extremes.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DateExtremes());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(2);

        const string ProjectionSql = """
            SELECT
                Id,
                CONVERT(VARCHAR(40), Dt2Min, 121),
                CONVERT(VARCHAR(40), Dt2Max, 121),
                CONVERT(VARCHAR(50), DtoMin, 121),
                CONVERT(VARCHAR(50), DtoMax, 121),
                CONVERT(VARCHAR(40), Dt2NoFrac, 121),
                CONVERT(VARCHAR(60), DecBig),
                CONVERT(VARCHAR(20), DecTight)
            FROM dbo.DateExtremes
            ORDER BY Id
            """;

        var sourceProjection = await ReadDateExtremesAsync(source, ProjectionSql);
        var targetProjection = await ReadDateExtremesAsync(target, ProjectionSql);
        targetProjection.ShouldBe(sourceProjection);
    }

    private static async Task<IReadOnlyList<string>> ReadLabelBytesAsync(SqlServerFixtureDatabase db)
    {
        var sql = """
            SELECT CONVERT(VARCHAR(MAX), CONVERT(VARBINARY(MAX), Label), 1)
            FROM dbo.UnicodeRows
            ORDER BY Id
            """;
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static async Task<IReadOnlyList<string>> ReadDateExtremesAsync(SqlServerFixtureDatabase db, string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            var fields = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                fields[i] = reader.GetValue(i)?.ToString() ?? "<null>";
            }
            result.Add(string.Join("|", fields));
        }
        return result;
    }
}
