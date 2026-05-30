using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class OperationalFeatureTests
{
    private readonly SqlServerContainerFixture _fixture;

    public OperationalFeatureTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExportPreflight_ValidSource_ReturnsPlannedManifest()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };

        var result = await new SqlDataBridgeExporter().PreflightAsync(source.ConnectionString, options);

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.Manifest.ShouldNotBeNull();
        result.Manifest.Tables.Select(t => t.FullName).ShouldBe(["dbo.IncludeMe"]);
        result.Manifest.Tables[0].ExportedRowCount.ShouldBe(0);
        result.Manifest.Tables[0].Columns.Select(c => c.Name).ShouldContain("KeepCol");
    }

    [Fact]
    public async Task PackageReader_ReadManifest_ReturnsStoredPackageMetadata()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        var manifest = await new DataPackageReader().ReadManifestAsync(sqlite.FilePath);

        manifest.PackageFormatVersion.ShouldBe(4);
        manifest.ApplicationVersion.ShouldNotBeEmpty();
        manifest.Tables.Select(t => t.FullName).ShouldBe(["dbo.IncludeMe"]);
        manifest.Tables[0].ExportedRowCount.ShouldBe(3);
        manifest.ImportOrder.ShouldBe(["dbo.IncludeMe"]);
        manifest.ContainsDacpac.ShouldBeFalse();
        manifest.DacpacSchemaScope.ShouldBeNull();
    }

    [Fact]
    public async Task ImportPreflight_TargetNotEmpty_ReturnsErrorWithoutImporting()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe() + """

            INSERT INTO dbo.IncludeMe (KeepCol, SkipCol) VALUES (N'existing', N'existing');
            """);
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        var result = await new SqlDataBridgeImporter().PreflightAsync(sqlite.FilePath, target.ConnectionString);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("must be empty", StringComparison.Ordinal));
        result.Manifest.ShouldNotBeNull();
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(1);
    }

    [Fact]
    public async Task ExportAndImport_ReportProgressAndWarnings()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe());
        await using var sqlite = new SqliteTempFileHarness();
        var exportProgress = new CollectingProgress();
        var importProgress = new CollectingProgress();
        var exportOptions = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.IncludeMe"],
            BatchSize = 3,
            LargeTableRowThreshold = 1,
            LargeTableBatchSize = 1,
            Progress = exportProgress
        };
        var importOptions = new ImportOptions
        {
            BatchSize = 3,
            LargeTableRowThreshold = 1,
            LargeTableBatchSize = 1,
            Progress = importProgress
        };

        var exportResult = await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString, importOptions);

        exportResult.Warnings.ShouldContain(w => w.Contains("Adaptive batching", StringComparison.Ordinal));
        importResult.Warnings.ShouldContain(w => w.Contains("Adaptive batching", StringComparison.Ordinal));
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.OperationStarted);
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.TableStarted);
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.RowsCopied);
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.TableCompleted);
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.Warning);
        exportProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.OperationCompleted);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.OperationStarted);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.TableStarted);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.RowsCopied);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.TableCompleted);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.Warning);
        importProgress.Events.Select(e => e.Kind).ShouldContain(BridgeProgressKind.OperationCompleted);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(3);
    }

    private sealed class CollectingProgress : IProgress<BridgeProgress>
    {
        public List<BridgeProgress> Events { get; } = [];

        public void Report(BridgeProgress value)
        {
            Events.Add(value);
        }
    }
}
