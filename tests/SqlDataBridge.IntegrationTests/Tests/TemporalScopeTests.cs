using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class TemporalScopeTests
{
    private readonly SqlServerContainerFixture _fixture;

    public TemporalScopeTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Export_TemporalSource_EmitsInformationalWarning()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var exportResult = await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        exportResult.Warnings.ShouldContain(w =>
            w.Contains("dbo.Department") && w.Contains("system-versioned temporal table"));
    }

    [Fact]
    public async Task Import_CurrentTableOnly_RoundTripsWithEmptyHistory()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath,
            new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.Department"] });
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(1);
        importResult.RowCount.ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Department")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.DepartmentHistory")).ShouldBe(0);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
    }

    [Fact]
    public async Task Import_HistoryTableOnly_RoundTripsWithEmptyCurrent()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath,
            new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.DepartmentHistory"] });
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(1);
        importResult.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Department")).ShouldBe(0);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.DepartmentHistory")).ShouldBe(3);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
    }

    [Fact]
    public async Task Import_NonTemporalSourceIntoTemporalTarget_AutoPopulatesPeriodWithoutSuspending()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_plain_source.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.LedgerTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Ledger")).ShouldBe(3);
        // Target stayed system-versioned and SQL Server filled in the period columns the package did not carry.
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Ledger'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Ledger WHERE ValidFrom IS NOT NULL")).ShouldBe(3);
    }

    [Fact]
    public async Task Preflight_NonTemporalSourceIntoTemporalTarget_DoesNotFailOnPeriodColumns()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_plain_source.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.LedgerTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var preflight = await new SqlDataBridgeImporter().PreflightAsync(sqlite.FilePath, target.ConnectionString);

        preflight.IsValid.ShouldBeTrue();
        preflight.Errors.ShouldBeEmpty();
    }
}
