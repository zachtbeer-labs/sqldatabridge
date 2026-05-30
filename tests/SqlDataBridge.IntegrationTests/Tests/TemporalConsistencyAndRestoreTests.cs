using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class TemporalConsistencyAndRestoreTests
{
    private readonly SqlServerContainerFixture _fixture;

    public TemporalConsistencyAndRestoreTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Import_InconsistentTemporalData_FailsWithConsistencyCheckOnButSucceedsWhenDisabled()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("temporal.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        // Corrupt the exported history so each row's period start is after its end (swap relies on SQLite
        // evaluating both SET expressions against the original row values).
        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await connection.ExecuteSqlAsync(
                "UPDATE zsb_data_dbo__departmenthistory SET ValidFrom = ValidTo, ValidTo = ValidFrom");
        }
        SqliteConnection.ClearAllPools();

        await using (var targetOn = await SqlServerFixtureDatabase.CreateAsync(_fixture))
        {
            await targetOn.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());
            var exception = await Should.ThrowAsync<BridgeException>(() =>
                new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, targetOn.ConnectionString));
            exception.Message.ShouldContain("consistency");
        }

        await using (var targetOff = await SqlServerFixtureDatabase.CreateAsync(_fixture))
        {
            await targetOff.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());
            var options = ImportOptions.Default;
            options.TemporalDataConsistencyCheck = false;
            var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, targetOff.ConnectionString, options);

            result.RowCount.ShouldBe(5);
            (await targetOff.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
        }
    }

    [Fact]
    public async Task SuspendThenRestore_RestoresVersioningAndIsIdempotent()
    {
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());

        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync();

        var temporals = await TemporalTableManager.DiscoverAsync(connection, null, CancellationToken.None);
        var suspensions = temporals.Select(t => new TemporalSuspension(t, DropPeriod: true)).ToArray();

        await TemporalTableManager.SuspendAsync(connection, suspensions, null, CancellationToken.None);
        await TemporalTableManager.SuspendAsync(connection, suspensions, null, CancellationToken.None); // re-run is a no-op
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldNotBe(2);

        await TemporalTableManager.RestoreAsync(connection, suspensions, dataConsistencyCheck: true, null, CancellationToken.None);
        await TemporalTableManager.RestoreAsync(connection, suspensions, dataConsistencyCheck: true, null, CancellationToken.None); // re-run is a no-op
        (await target.ScalarIntAsync("SELECT temporal_type FROM sys.tables WHERE name = 'Department'")).ShouldBe(2);
    }

    [Fact]
    public async Task TryRestoreBestEffort_WhenRestoreImpossible_LeavesWarningInsteadOfThrowing()
    {
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.DepartmentTemporal());

        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync();

        var temporals = await TemporalTableManager.DiscoverAsync(connection, null, CancellationToken.None);
        var suspensions = temporals.Select(t => new TemporalSuspension(t, DropPeriod: true)).ToArray();

        await TemporalTableManager.SuspendAsync(connection, suspensions, null, CancellationToken.None);
        // Make re-enabling versioning impossible: after the period is dropped, remove a period column so the
        // restore's ADD PERIOD fails. (Dropping the history table would not work — SQL Server just recreates
        // one when HISTORY_TABLE names a missing table.)
        await target.ExecuteSqlAsync("ALTER TABLE dbo.Department DROP COLUMN ValidTo");

        var warnings = new List<string>();
        await TemporalTableManager.TryRestoreBestEffortAsync(connection, suspensions, dataConsistencyCheck: true, warnings);

        warnings.ShouldContain(w => w.Contains("dbo.Department") && w.Contains("SYSTEM_VERSIONING = ON"));
    }
}
