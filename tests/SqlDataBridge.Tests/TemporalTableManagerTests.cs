using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;
using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class TemporalTableManagerTests
{
    private static TemporalTable Temporal(
        string schema = "dbo",
        string current = "Department",
        string history = "DepartmentHistory",
        string start = "ValidFrom",
        string end = "ValidTo",
        int retentionPeriod = -1,
        string? retentionUnit = null) =>
        new(new TableName(schema, current), new TableName(schema, history), start, end, retentionPeriod, retentionUnit);

    private static TemporalSuspension Suspend(bool dropPeriod, TemporalTable? table = null) =>
        new(table ?? Temporal(), dropPeriod);

    private static ColumnMetadata Col(TableName table, string name) =>
        new(table, name, 0, "datetime2", 8, 7, 7, IsNullable: false, IsIdentity: false, IsComputed: false, CollationName: null, IsExcluded: false);

    private static TableMetadata Meta(string schema, string name, params string[] columns)
    {
        var tableName = new TableName(schema, name);
        var cols = columns.Select(c => Col(tableName, c)).ToArray();
        return new TableMetadata(tableName, $"zsb_data_{schema}__{name}".ToLowerInvariant(), cols);
    }

    // ----- ResolveSuspensions ------------------------------------------------------------------

    [Fact]
    public void ResolveSuspensions_CurrentInScopeWithPeriodColumns_IncludedAndDropsPeriod()
    {
        var result = TemporalTableManager.ResolveSuspensions(
            new[] { Temporal() },
            new[] { Meta("dbo", "Department", "DepartmentId", "ValidFrom", "ValidTo") });

        var suspension = result.ShouldHaveSingleItem();
        suspension.DropPeriod.ShouldBeTrue();
        suspension.Table.Current.Name.ShouldBe("Department");
    }

    [Fact]
    public void ResolveSuspensions_OnlyHistoryInScope_IncludedWithoutDroppingPeriod()
    {
        var result = TemporalTableManager.ResolveSuspensions(
            new[] { Temporal() },
            new[] { Meta("dbo", "DepartmentHistory", "DepartmentId", "ValidFrom", "ValidTo") });

        result.ShouldHaveSingleItem().DropPeriod.ShouldBeFalse();
    }

    [Fact]
    public void ResolveSuspensions_CurrentInScopeButPeriodColumnsExcluded_NotSuspended()
    {
        // Current table is in scope but its period columns are not exported (e.g. excluded). Versioning can
        // stay on and SQL Server auto-populates the period, so the pair must not be suspended.
        var result = TemporalTableManager.ResolveSuspensions(
            new[] { Temporal() },
            new[] { Meta("dbo", "Department", "DepartmentId") });

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveSuspensions_MatchesScopeCaseInsensitively()
    {
        var result = TemporalTableManager.ResolveSuspensions(
            new[] { Temporal() },
            new[] { Meta("DBO", "DEPARTMENT", "ValidFrom", "ValidTo") });

        result.ShouldHaveSingleItem().DropPeriod.ShouldBeTrue();
    }

    [Fact]
    public void ResolveSuspensions_MultiplePairs_ReturnsOnlyInScope()
    {
        var department = Temporal();
        var product = Temporal(current: "Product", history: "ProductHistory");

        var result = TemporalTableManager.ResolveSuspensions(
            new[] { department, product },
            new[] { Meta("dbo", "Department", "ValidFrom", "ValidTo") });

        result.ShouldHaveSingleItem().Table.Current.Name.ShouldBe("Department");
    }

    [Fact]
    public void ResolveSuspensions_EmptyInputs_ReturnEmpty()
    {
        TemporalTableManager.ResolveSuspensions(Array.Empty<TemporalTable>(),
            new[] { Meta("dbo", "Department", "ValidFrom", "ValidTo") }).ShouldBeEmpty();
        TemporalTableManager.ResolveSuspensions(new[] { Temporal() },
            Array.Empty<TableMetadata>()).ShouldBeEmpty();
    }

    // ----- BuildSuspendSql ---------------------------------------------------------------------

    [Fact]
    public void BuildSuspendSql_DropPeriodTrue_DisablesVersioningThenDropsPeriod()
    {
        var sql = TemporalTableManager.BuildSuspendSql(Suspend(dropPeriod: true));

        sql.ShouldContain("SET (SYSTEM_VERSIONING = OFF)");
        sql.ShouldContain("DROP PERIOD FOR SYSTEM_TIME");
        sql.ShouldContain("[dbo].[Department]");
        // Guarded so a re-run is a no-op.
        sql.ShouldContain("temporal_type = 2");
        // SET OFF must precede DROP PERIOD (dropping a period on a versioned table is disallowed).
        sql.IndexOf("SYSTEM_VERSIONING = OFF", StringComparison.Ordinal)
            .ShouldBeLessThan(sql.IndexOf("DROP PERIOD", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSuspendSql_DropPeriodFalse_OnlyDisablesVersioning()
    {
        var sql = TemporalTableManager.BuildSuspendSql(Suspend(dropPeriod: false));

        sql.ShouldContain("SET (SYSTEM_VERSIONING = OFF)");
        sql.ShouldNotContain("DROP PERIOD");
    }

    // ----- BuildRestoreSql ---------------------------------------------------------------------

    [Fact]
    public void BuildRestoreSql_DropPeriodTrue_AddsPeriodThenEnablesVersioning_ConsistencyOnByDefault()
    {
        var sql = TemporalTableManager.BuildRestoreSql(Suspend(dropPeriod: true), dataConsistencyCheck: true);

        sql.ShouldContain("ADD PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])");
        sql.ShouldContain("HISTORY_TABLE = [dbo].[DepartmentHistory]");
        sql.ShouldContain("DATA_CONSISTENCY_CHECK = ON");
        // ADD PERIOD must precede SET ON (you cannot enable versioning without a period).
        sql.IndexOf("ADD PERIOD", StringComparison.Ordinal)
            .ShouldBeLessThan(sql.IndexOf("SYSTEM_VERSIONING = ON", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRestoreSql_DropPeriodFalse_OmitsAddPeriod_StillEnablesVersioning()
    {
        var sql = TemporalTableManager.BuildRestoreSql(Suspend(dropPeriod: false), dataConsistencyCheck: true);

        sql.ShouldNotContain("ADD PERIOD");
        sql.ShouldContain("SYSTEM_VERSIONING = ON");
    }

    [Fact]
    public void BuildRestoreSql_OmitsConsistencyCheck_WhenDisabled()
    {
        var sql = TemporalTableManager.BuildRestoreSql(Suspend(dropPeriod: true), dataConsistencyCheck: false);

        sql.ShouldContain("DATA_CONSISTENCY_CHECK = OFF");
        sql.ShouldNotContain("DATA_CONSISTENCY_CHECK = ON");
    }

    [Fact]
    public void BuildRestoreSql_FiniteRetention_ReappliesHistoryRetentionPeriod()
    {
        var table = Temporal(retentionPeriod: 6, retentionUnit: "MONTH");

        var sql = TemporalTableManager.BuildRestoreSql(new TemporalSuspension(table, DropPeriod: true), dataConsistencyCheck: true);

        sql.ShouldContain("HISTORY_RETENTION_PERIOD = 6 MONTH");
    }

    [Fact]
    public void BuildRestoreSql_InfiniteRetention_OmitsRetentionClause()
    {
        var sql = TemporalTableManager.BuildRestoreSql(Suspend(dropPeriod: true), dataConsistencyCheck: true);

        sql.ShouldNotContain("HISTORY_RETENTION_PERIOD");
    }

    [Fact]
    public void BuildRestoreSql_UsesCustomPeriodColumnNames()
    {
        var table = Temporal(start: "LastUpdateDate", end: "LastUpdateValidTo");

        var sql = TemporalTableManager.BuildRestoreSql(new TemporalSuspension(table, DropPeriod: true), dataConsistencyCheck: true);

        sql.ShouldContain("ADD PERIOD FOR SYSTEM_TIME ([LastUpdateDate], [LastUpdateValidTo])");
    }

    [Fact]
    public void BuildSuspendAndRestoreSql_EscapeOddIdentifiers()
    {
        var table = Temporal(current: "Odd]Name", history: "Odd]Name_History");
        var suspend = TemporalTableManager.BuildSuspendSql(new TemporalSuspension(table, DropPeriod: true));
        var restore = TemporalTableManager.BuildRestoreSql(new TemporalSuspension(table, DropPeriod: true), dataConsistencyCheck: true);

        suspend.ShouldContain("[dbo].[Odd]]Name]");
        suspend.ShouldContain("OBJECT_ID(N'[dbo].[Odd]]Name]')");
        restore.ShouldContain("HISTORY_TABLE = [dbo].[Odd]]Name_History]");
    }

    // ----- DescribeSuspend ---------------------------------------------------------------------

    [Fact]
    public void DescribeSuspend_DropPeriodTrue_MentionsPeriodAndVersioning()
    {
        var message = TemporalTableManager.DescribeSuspend(Suspend(dropPeriod: true));

        message.ShouldContain("dbo.Department");
        message.ShouldContain("dbo.DepartmentHistory");
        message.ShouldContain("system versioning");
        message.ShouldContain("period");
    }

    [Fact]
    public void DescribeSuspend_DropPeriodFalse_OmitsPeriodNote()
    {
        var message = TemporalTableManager.DescribeSuspend(Suspend(dropPeriod: false));

        message.ShouldContain("system versioning");
        message.ShouldNotContain("period dropped");
    }

    // ----- TryRestoreBestEffortAsync (offline) -------------------------------------------------

    [Fact]
    public async Task TryRestoreBestEffortAsync_WhenRestoreFails_AddsOneDeduplicatedWarningPerTable()
    {
        // A closed connection makes every restore throw; the best-effort path must swallow and warn, not throw.
        await using var connection = new SqlConnection("Server=localhost;Database=does-not-matter;");
        var warnings = new List<string>();
        var suspension = Suspend(dropPeriod: true);

        await TemporalTableManager.TryRestoreBestEffortAsync(
            connection, new[] { suspension, suspension }, dataConsistencyCheck: true, warnings);

        var warning = warnings.ShouldHaveSingleItem();
        warning.ShouldContain("dbo.Department");
        warning.ShouldContain("SYSTEM_VERSIONING = ON");
    }
}
