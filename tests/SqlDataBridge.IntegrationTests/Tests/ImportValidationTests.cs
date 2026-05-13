using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class ImportValidationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public ImportValidationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Import_MissingTargetTable_FailsBeforeImport()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("identity_fk.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IdentityForeignKeys(includeChild: false));
        await using var sqlite = new SqliteTempFileHarness();
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("Target table 'dbo.Child' does not exist");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Parent")).ShouldBe(0);
    }

    [Fact]
    public async Task Import_TargetTableNotEmpty_FailsBeforeImport()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("identity_fk.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IdentityForeignKeys() + """

            INSERT INTO dbo.Parent (ParentName) VALUES ('already here');
            """);
        await using var sqlite = new SqliteTempFileHarness();
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("must be empty");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Parent")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Child")).ShouldBe(0);
    }

    [Fact]
    public async Task Import_MissingTargetColumn_FailsBeforeImport()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe(includeKeepCol: false));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("Target column 'dbo.IncludeMe.KeepCol' does not exist");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(0);
    }

    [Fact]
    public async Task Import_ExtraRequiredTargetColumn_FailsBeforeImport()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe(requiredExtra: true));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("Extra target column 'dbo.IncludeMe.RequiredExtra' is not nullable or defaulted");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(0);
    }

    [Fact]
    public async Task Import_RowCountMismatch_FailsAfterImport()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe());
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);
        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await connection.ExecuteSqlAsync("UPDATE zsb_table_stats SET exported_row_count = exported_row_count + 1");
        }

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("Imported row count for 'dbo.IncludeMe' was 3, expected 4");
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(3);
    }

    [Fact]
    public async Task Import_MissingMetadataTable_FailsPackageValidation()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);
        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await connection.ExecuteSqlAsync("DROP TABLE zsb_import_plan");
        }

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("required metadata table 'zsb_import_plan' is missing");
    }

    [Fact]
    public async Task Import_MissingDataTable_FailsPackageValidation()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);
        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await connection.ExecuteSqlAsync("DROP TABLE zsb_data_dbo__includeme");
        }

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("data table 'zsb_data_dbo__includeme'");
    }

    [Fact]
    public async Task Import_MissingRowCountMetadata_FailsPackageValidation()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only, Tables = ["dbo.IncludeMe"] };
        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);
        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await connection.ExecuteSqlAsync("DELETE FROM zsb_table_stats");
        }

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString));

        exception.Message.ShouldContain("row-count metadata is missing for 'dbo.IncludeMe'");
    }
}
