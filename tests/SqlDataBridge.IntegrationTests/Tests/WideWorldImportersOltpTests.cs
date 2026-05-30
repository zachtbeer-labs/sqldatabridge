using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

// Opt-in, real-world coverage against Microsoft's WideWorldImporters OLTP sample. Gated on a locally-provided
// WideWorldImporters-Full.bak (gitignored) — see SharedWideWorldImportersSource / OptionalFixture. When the backup
// is absent every [Fact] logs [skip] and returns, so CI and clean clones stay green.
//
// WWI exercises a realistic combination the focused fixtures cannot: many system-versioned reference tables with
// pre-populated history (deterministic — no clock-tick dependence), sequence-backed surrogate keys, dense FK
// graphs, JSON and computed columns. Two of its headline features are explicitly unsupported (geography columns and
// in-memory OLTP tables), which is what the gap test below pins.
[Collection(nameof(SqlServerCollection))]
public sealed class WideWorldImportersOltpTests
{
    private const string BakFileName = "WideWorldImporters-Full.bak";
    private const string BakEnvironmentVariable = "SQLDATABRIDGE_WWI_OLTP_BAK";

    // Small, system-versioned, sequence-backed reference tables with no geography/in-memory dependency — the curated
    // supported slice. Each has a sibling *_Archive history table the exporter discovers and round-trips automatically.
    private static readonly string[] SupportedTemporalTables =
    [
        "Warehouse.Colors",
        "Warehouse.PackageTypes",
        "Application.PaymentMethods"
    ];

    private readonly SqlServerContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WideWorldImportersOltpTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // Goal A — curated green round-trip: export a supported temporal/sequence slice with a selected-tables dacpac,
    // deploy the schema into a fresh target, load the data, and assert the temporal history and row counts match the
    // source byte-for-byte at real-schema fidelity.
    [Fact]
    public async Task Oltp_SupportedTemporalSlice_RoundTrips()
    {
        var source = await SharedWideWorldImportersSource.TryGetAsync(_fixture, BakFileName, BakEnvironmentVariable, _output);
        if (source is null)
        {
            return;
        }

        // Counts captured from the source up front so the assertions track the actual backup, not hard-coded numbers
        // (WWI row counts differ slightly across sample releases).
        var expectedAllCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        long expectedTotalRows = 0;
        foreach (var table in SupportedTemporalTables)
        {
            (await source.ScalarIntAsync($"SELECT temporal_type FROM sys.tables WHERE object_id = OBJECT_ID('{table}')"))
                .ShouldBe(2, $"Pre-condition: source '{table}' should be system-versioned in this WWI backup.");
            var allCount = await source.ScalarIntAsync($"SELECT COUNT(*) FROM {table} FOR SYSTEM_TIME ALL");
            expectedAllCounts[table] = allCount;
            expectedTotalRows += allCount;
            _output.WriteLine($"source {table}: FOR SYSTEM_TIME ALL = {allCount}");
        }

        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();

        var exportOptions = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = SupportedTemporalTables,
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            DacpacCaptureOptions = new DacpacCaptureOptions { SchemaScope = DacpacSchemaScope.SelectedExportTables },
            CommandTimeout = 600
        };

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions
            {
                SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
                ValidationCommandTimeout = 600,
                BulkCopyTimeout = 600
            });

        // Current + history rows for every selected table land in the target.
        result.RowCount.ShouldBe(expectedTotalRows);

        foreach (var table in SupportedTemporalTables)
        {
            (await target.ScalarIntAsync($"SELECT temporal_type FROM sys.tables WHERE object_id = OBJECT_ID('{table}')"))
                .ShouldBe(2, $"'{table}' should be re-created as system-versioned on the target.");
            (await target.ScalarIntAsync($"SELECT COUNT(*) FROM {table} FOR SYSTEM_TIME ALL"))
                .ShouldBe(expectedAllCounts[table], $"'{table}' history did not round-trip.");
        }

        // The selected-tables dacpac must also carry the sequence objects referenced by the surrogate-key defaults,
        // otherwise the schema deploy of those defaults would have failed. Proves sequence dependencies travel.
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.sequences"))
            .ShouldBeGreaterThan(0, "Expected the sequence(s) backing the selected tables' keys to be deployed via the dacpac.");
    }

    // Goal B — gap documentation: inventory what WWI exercises and pin the current limitation set. A full-database
    // export preflight must fail on the first unsupported type (geography), and the inventory is dumped to test
    // output for human review. If a future change adds geography support, this test fails loudly and forces an update.
    [Fact]
    public async Task Oltp_FullDatabaseExport_PinsKnownUnsupportedSurface()
    {
        var source = await SharedWideWorldImportersSource.TryGetAsync(_fixture, BakFileName, BakEnvironmentVariable, _output);
        if (source is null)
        {
            return;
        }

        var spatialColumns = await source.ScalarIntAsync(
            """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            WHERE t.name IN ('geography', 'geometry', 'hierarchyid', 'sql_variant')
            """);
        var memoryOptimizedTables = await source.ScalarIntAsync("SELECT COUNT(*) FROM sys.tables WHERE is_memory_optimized = 1");
        var temporalTables = await source.ScalarIntAsync("SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2");
        var sequences = await source.ScalarIntAsync("SELECT COUNT(*) FROM sys.sequences");
        var fullTextIndexes = await source.ScalarIntAsync("SELECT COUNT(*) FROM sys.fulltext_indexes");

        _output.WriteLine("WideWorldImporters OLTP feature inventory:");
        _output.WriteLine($"  unsupported-type columns (geography/geometry/hierarchyid/sql_variant): {spatialColumns}");
        _output.WriteLine($"  memory-optimized tables: {memoryOptimizedTables}");
        _output.WriteLine($"  system-versioned temporal tables: {temporalTables}");
        _output.WriteLine($"  sequences: {sequences}");
        _output.WriteLine($"  full-text indexes: {fullTextIndexes}");

        // These are the load-bearing reasons a naive whole-database export can't round-trip today.
        spatialColumns.ShouldBeGreaterThan(0, "WWI OLTP is expected to contain geography columns; if not, the backup shape changed.");
        temporalTables.ShouldBeGreaterThan(0);

        var preflight = await new SqlDataBridgeExporter().PreflightAsync(
            source.ConnectionString,
            new ExportOptions { CommandTimeout = 600 });

        preflight.IsValid.ShouldBeFalse("A whole-database export of WWI OLTP must be blocked by its unsupported types today.");
        preflight.Errors.ShouldContain(e => e.Contains("Unsupported included type", StringComparison.Ordinal));
        preflight.Errors.ShouldContain(e => e.Contains("geography", StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"Preflight (full database) error: {preflight.Errors.First(e => e.Contains("Unsupported included type", StringComparison.Ordinal))}");
    }
}
