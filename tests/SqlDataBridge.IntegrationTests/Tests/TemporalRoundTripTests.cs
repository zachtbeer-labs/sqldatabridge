using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class TemporalRoundTripTests
{
    private readonly SqlServerContainerFixture _fixture;

    public TemporalRoundTripTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Import_SystemVersionedTemporalTable_RoundTripsCurrentAndHistoryWithExactPeriods()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(2);
        importResult.RowCount.ShouldBe(5);
        importResult.Warnings.ShouldContain(w => w.Contains("Department") && w.Contains("system versioning"));

        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Department")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.DepartmentHistory")).ShouldBe(3);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Department FOR SYSTEM_TIME ALL")).ShouldBe(5);

        (await target.ScalarStringAsync(
            "SELECT STRING_AGG(CONCAT(DepartmentId, ':', DepartmentName, '=', ManagerId), '|') WITHIN GROUP (ORDER BY DepartmentId) FROM dbo.Department"))
            .ShouldBe("1:Sales=11|2:Engineering=21");

        var sourceDump = await DumpDepartmentAsync(source);
        var targetDump = await DumpDepartmentAsync(target);
        targetDump.ShouldBe(sourceDump);
    }

    [Fact]
    public async Task Import_TemporalTable_DeployDacpacSchemaThenLoadData()
    {
        // Realistic end-to-end: the dacpac (not a hand-written target script) recreates the temporal tables.
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath,
            new ExportOptions { SchemaCaptureMode = SchemaCaptureMode.Dacpac, CommandTimeout = 120 });
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        importResult.RowCount.ShouldBe(5);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Department FOR SYSTEM_TIME ALL")).ShouldBe(5);
    }

    [Fact]
    public async Task Import_TemporalTable_PreservesCustomPeriodNamesAndFiniteRetentionPeriod()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_realistic.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.SubscriptionTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Subscription")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Subscription_History")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Subscription'")).ShouldBe(2);

        // The bug this guards: SET SYSTEM_VERSIONING = OFF/ON would otherwise reset retention to INFINITE (-1).
        (await target.ScalarIntAsync("SELECT history_retention_period FROM sys.tables WHERE name = 'Subscription'")).ShouldBe(3);
        (await target.ScalarStringAsync("SELECT history_retention_period_unit_desc FROM sys.tables WHERE name = 'Subscription'")).ShouldBe("MONTH");

        // Custom-named period columns round-trip byte-for-byte.
        var sourceDump = await DumpSubscriptionAsync(source);
        var targetDump = await DumpSubscriptionAsync(target);
        targetDump.ShouldBe(sourceDump);
    }

    [Fact]
    public async Task Import_MultipleTemporalTables_AllSuspendedAndRestored()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_multi.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.MultiTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(4);
        importResult.RowCount.ShouldBe(5);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Region'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Team'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Region FOR SYSTEM_TIME ALL")).ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Team FOR SYSTEM_TIME ALL")).ShouldBe(2);
    }

    [Fact]
    public async Task Import_TemporalTableWithForeignKey_LoadsParentBeforeChildAndRestores()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_fk.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.TemporalWithForeignKey());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(5);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Office")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Worker'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Worker FOR SYSTEM_TIME ALL")).ShouldBe(3);
        // FK still enforced after the suspend/restore round-trip.
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_Worker_Office'")).ShouldBe(1);
    }

    [Fact]
    public async Task Import_TemporalTableWithHiddenPeriodColumns_RoundTrips()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal_hidden.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.HiddenPeriodTemporal());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Flag'")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Flag FOR SYSTEM_TIME ALL")).ShouldBe(3);
        (await target.ScalarStringAsync(
            "SELECT STRING_AGG(FlagName, ',') WITHIN GROUP (ORDER BY FlagId) FROM dbo.Flag"))
            .ShouldBe("x2,y");
    }

    private static Task<string> DumpDepartmentAsync(SqlServerFixtureDatabase db)
    {
        return db.ScalarStringAsync("""
            SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), CONCAT(
                       DepartmentId, '|', DepartmentName, '|', ManagerId, '|',
                       CONVERT(VARCHAR(33), ValidFrom, 126), '|', CONVERT(VARCHAR(33), ValidTo, 126))), CHAR(10))
                   WITHIN GROUP (ORDER BY DepartmentId, ValidFrom)
            FROM dbo.Department FOR SYSTEM_TIME ALL
            """);
    }

    private static Task<string> DumpSubscriptionAsync(SqlServerFixtureDatabase db)
    {
        return db.ScalarStringAsync("""
            SELECT STRING_AGG(CONVERT(NVARCHAR(MAX), CONCAT(
                       SubscriptionId, '|', CustomerId, '|', PlanName, '|',
                       CONVERT(VARCHAR(33), LastUpdateDate, 126), '|', CONVERT(VARCHAR(33), LastUpdateValidTo, 126))), CHAR(10))
                   WITHIN GROUP (ORDER BY SubscriptionId, LastUpdateDate)
            FROM dbo.Subscription FOR SYSTEM_TIME ALL
            """);
    }
}
