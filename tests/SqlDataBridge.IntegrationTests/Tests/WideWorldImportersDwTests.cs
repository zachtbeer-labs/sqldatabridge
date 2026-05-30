using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

// Opt-in, real-world coverage against Microsoft's WideWorldImportersDW (data-warehouse) sample. Gated on a locally-
// provided WideWorldImportersDW-Full.bak (gitignored) exactly like the OLTP variant; absent backup => [skip].
//
// The DW database is the bulk-volume / columnstore shape: large fact tables with clustered columnstore indexes and
// few unsupported types. The round-trip test proves a columnstore fact survives export→import at scale and that the
// columnstore index is recreated from the dacpac; the gap test pins that the index (like all schema) only travels
// via the dacpac path — a data-only package cannot recreate the fact table or its columnstore.
[Collection(nameof(SqlServerCollection))]
public sealed class WideWorldImportersDwTests
{
    private const string BakFileName = "WideWorldImportersDW-Full.bak";
    private const string BakEnvironmentVariable = "SQLDATABRIDGE_WWI_DW_BAK";

    // A clustered-columnstore fact of moderate size — big enough to exercise the bulk-copy path, small enough to keep
    // the opt-in test reasonable.
    private const string FactTable = "Fact.Purchase";
    private const string FactSchemaName = "Fact";
    private const string FactTableName = "Purchase";

    private readonly SqlServerContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WideWorldImportersDwTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // Goal A — green round-trip of a columnstore fact: selected-tables dacpac deploy recreates the fact (including its
    // columnstore index), then the bulk-copy path lands every row.
    [Fact]
    public async Task Dw_ColumnstoreFact_RoundTripsAndRecreatesColumnstore()
    {
        var source = await SharedWideWorldImportersSource.TryGetAsync(_fixture, BakFileName, BakEnvironmentVariable, _output);
        if (source is null)
        {
            return;
        }

        var expectedRows = await source.ScalarIntAsync($"SELECT COUNT(*) FROM {FactTable}");
        var sourceColumnstoreIndexes = await CountColumnstoreIndexesAsync(source);
        _output.WriteLine($"source {FactTable}: rows = {expectedRows}, columnstore indexes = {sourceColumnstoreIndexes}");
        sourceColumnstoreIndexes.ShouldBeGreaterThan(0, $"Pre-condition: source '{FactTable}' should carry a columnstore index in this WWI DW backup.");

        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();

        var exportOptions = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = [FactTable],
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

        result.RowCount.ShouldBe(expectedRows);
        (await target.ScalarIntAsync($"SELECT COUNT(*) FROM {FactTable}")).ShouldBe(expectedRows);
        (await CountColumnstoreIndexesAsync(target))
            .ShouldBeGreaterThan(0, "The columnstore index should be recreated on the target from the deployed dacpac.");
    }

    // Goal B — gap documentation: a data-only package (SchemaCaptureMode.None) carries no schema, so importing it into
    // a fresh target cannot recreate the fact table or its columnstore. This is what "columnstore is schema-only via
    // dacpac" means operationally — contrast with the DeployDacpac round-trip above.
    [Fact]
    public async Task Dw_DataOnlyExport_CannotRecreateFactWithoutDacpac()
    {
        var source = await SharedWideWorldImportersSource.TryGetAsync(_fixture, BakFileName, BakEnvironmentVariable, _output);
        if (source is null)
        {
            return;
        }

        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();

        var exportOptions = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = [FactTable],
            SchemaCaptureMode = SchemaCaptureMode.None,
            CommandTimeout = 600
        };

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);

        // No schema in the package and an empty target => the import has nothing to load into.
        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(
                sqlite.FilePath,
                target.ConnectionString,
                new ImportOptions { ValidationCommandTimeout = 600 }));

        exception.Message.ShouldContain("does not exist");
        _output.WriteLine($"Data-only import into a fresh target failed as expected: {exception.Message}");
    }

    private static Task<int> CountColumnstoreIndexesAsync(SqlServerFixtureDatabase db)
    {
        // Index types 5 (clustered columnstore) and 6 (nonclustered columnstore).
        return db.ScalarIntAsync(
            $"""
            SELECT COUNT(*)
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = '{FactSchemaName}' AND t.name = '{FactTableName}' AND i.type IN (5, 6)
            """);
    }
}
