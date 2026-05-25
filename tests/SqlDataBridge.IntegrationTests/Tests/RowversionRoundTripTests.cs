using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class RowversionRoundTripTests
{
    private readonly SqlServerContainerFixture _fixture;

    public RowversionRoundTripTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Export_RowversionColumn_EmitsWarningAndStoresBlob()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("rowversion.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var exportResult = await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        exportResult.TableCount.ShouldBe(1);
        exportResult.RowCount.ShouldBe(3);
        exportResult.Warnings.ShouldContain(w => w.Contains("RvAudit") && w.Contains("Rv"));

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.RvAudit");
        // SQL Server reports the rowversion column as the `timestamp` system type.
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_columns c
            INNER JOIN zsb_tables t ON t.id = c.table_id
            WHERE t.source_schema = 'dbo'
              AND t.source_table = 'RvAudit'
              AND c.column_name = 'Rv'
              AND c.sql_server_type_name = 'timestamp'
            """)).ShouldBe(1);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM pragma_table_info('zsb_data_dbo__rvaudit') WHERE name = 'Rv' AND type = 'BLOB'")).ShouldBe(1);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__rvaudit WHERE length(Rv) = 8")).ShouldBe(3);
    }

    [Fact]
    public async Task Import_RowversionColumn_SkipsBytesAndLetsServerGenerate()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("rowversion.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.RvAudit());
        // Bump the target's @@DBTS past the source's so identical-counter coincidence
        // cannot make this test false-pass. Then truncate so the table is empty for import.
        await target.ExecuteSqlAsync("""
            INSERT INTO dbo.RvAudit (Name) VALUES (N'bump1'), (N'bump2'), (N'bump3'), (N'bump4'), (N'bump5');
            DELETE FROM dbo.RvAudit;
            DBCC CHECKIDENT('dbo.RvAudit', RESEED, 0);
            """);
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(1);
        importResult.RowCount.ShouldBe(3);
        importResult.Warnings.ShouldContain(w => w.Contains("RvAudit") && w.Contains("Rv"));

        (await target.ScalarStringAsync("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Id) FROM dbo.RvAudit"))
            .ShouldBe("alpha,beta,gamma");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.RvAudit WHERE Rv IS NULL")).ShouldBe(0);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.RvAudit WHERE DATALENGTH(Rv) = 8")).ShouldBe(3);

        // Target rowversions were generated locally. Because we bumped @@DBTS before
        // the import, the smallest target Rv must exceed the largest source Rv —
        // proves SQL Server is regenerating values rather than copying source bytes.
        var sourceMax = await source.ScalarStringAsync(
            "SELECT CONVERT(VARCHAR(20), MAX(Rv), 1) FROM dbo.RvAudit");
        var targetMin = await target.ScalarStringAsync(
            "SELECT CONVERT(VARCHAR(20), MIN(Rv), 1) FROM dbo.RvAudit");
        string.CompareOrdinal(targetMin, sourceMax).ShouldBeGreaterThan(0,
            $"Target min Rv {targetMin} should exceed source max Rv {sourceMax} after @@DBTS bump.");
    }

    [Fact]
    public async Task Import_TargetHasRowversion_NoExtraColumnError()
    {
        // Source schema deliberately omits the rowversion column to prove that a
        // target-only rowversion does not trip the "extra target column" check.
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("rowversion_no_rv.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.RvAudit(includeRowversion: true));
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(1);
        importResult.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.RvAudit")).ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.RvAudit WHERE Rv IS NOT NULL")).ShouldBe(3);
    }
}
