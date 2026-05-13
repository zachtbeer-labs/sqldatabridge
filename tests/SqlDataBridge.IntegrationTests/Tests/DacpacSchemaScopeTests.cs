using Microsoft.Data.Sqlite;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class DacpacSchemaScopeTests
{
    private readonly SqlServerContainerFixture _fixture;

    public DacpacSchemaScopeTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Export_SelectedTableDacpac_StoresScopeAndOnlySelectedTablesInModel()
    {
        await using var source = await CreateSourceAsync();
        await using var sqlite = new SqliteTempFileHarness();
        var options = SelectedTableDacpacExportOptions("dbo.SelectedParent", "dbo.SelectedChild");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.ScalarStringAsync("SELECT schema_scope FROM zsb_schema_packages WHERE id = 1"))
            .ShouldBe(nameof(DacpacSchemaScope.SelectedExportTables));

        using var model = await LoadStoredDacpacModelAsync(sqlite.FilePath);
        ModelContainsTable(model, "dbo", "SelectedParent").ShouldBeTrue();
        ModelContainsTable(model, "dbo", "SelectedChild").ShouldBeTrue();
        ModelContainsTable(model, "dbo", "UnselectedTable").ShouldBeFalse();
        ModelContainsTable(model, "dbo", "CrossScopeChild").ShouldBeFalse();
        ModelContainsTable(model, "tenant", "SelectedThing").ShouldBeFalse();
    }

    [Fact]
    public async Task Export_SelectedTableDacpac_ModelIncludesTableChildren()
    {
        await using var source = await CreateSourceAsync();
        await using var sqlite = new SqliteTempFileHarness();
        var options = SelectedTableDacpacExportOptions("dbo.SelectedParent", "dbo.SelectedChild", "tenant.SelectedThing");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        using var model = await LoadStoredDacpacModelAsync(sqlite.FilePath);
        ModelContainsObjectNamed(model, ModelSchema.Index, "IX_SelectedChild_ParentId").ShouldBeTrue();
        ModelContainsObjectNamed(model, ModelSchema.ForeignKeyConstraint, "FK_SelectedChild_SelectedParent").ShouldBeTrue();
        ModelContainsObjectNamed(model, ModelSchema.CheckConstraint, "CK_SelectedChild_Qty").ShouldBeTrue();
        ModelContainsObjectNamed(model, ModelSchema.UniqueConstraint, "UQ_SelectedThing_Name").ShouldBeTrue();
    }

    [Fact]
    public async Task Import_SelectedTableDacpac_DeploysSchemaAndImportsRows()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.SelectedParent", "dbo.SelectedChild");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        result.TableCount.ShouldBe(2);
        result.RowCount.ShouldBe(5);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.SelectedParent")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.SelectedChild WHERE ExtendedPrice = Qty * UnitPrice")).ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_SelectedChild_ParentId'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_SelectedChild_SelectedParent'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_SelectedChild_Qty'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'UnselectedTable'")).ShouldBe(0);
    }

    [Fact]
    public async Task Import_SelectedTableDacpac_DeploysOverExistingCompatibleTarget()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync("""
            CREATE TABLE dbo.SelectedParent (
                ParentId INT IDENTITY(1,1) NOT NULL,
                ParentCode NVARCHAR(20) NOT NULL,
                ParentName NVARCHAR(50) NOT NULL,
                CONSTRAINT PK_SelectedParent PRIMARY KEY CLUSTERED (ParentId)
            );
            """);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.SelectedParent");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.SelectedParent")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_SelectedParent_Name'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_SelectedParent_Code'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_SelectedParent_Code'")).ShouldBe(1);
    }

    [Fact]
    public async Task Import_SelectedTableDacpac_DeploysNonDboSchemaAndImportsRows()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("tenant.SelectedThing");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.schemas WHERE name = 'tenant'")).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM tenant.SelectedThing")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_SelectedThing_Name'")).ShouldBe(1);
    }

    [Fact]
    public async Task Export_SelectedTableDacpac_CrossScopeForeignKeyWarnsAndDoesNotIncludeReferencedTable()
    {
        await using var source = await CreateSourceAsync();
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.CrossScopeChild");

        var result = await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);

        result.Warnings.ShouldContain(w => w.Contains("foreign key to unselected table 'dbo.UnselectedTable'", StringComparison.Ordinal));
        using var model = await LoadStoredDacpacModelAsync(sqlite.FilePath);
        ModelContainsTable(model, "dbo", "CrossScopeChild").ShouldBeTrue();
        ModelContainsTable(model, "dbo", "UnselectedTable").ShouldBeFalse();
    }

    [Fact]
    public async Task Import_SelectedTableDacpac_CrossScopeForeignKeyIsSkippedAndRowsImport()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.CrossScopeChild");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.CrossScopeChild")).ShouldBe(2);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'UnselectedTable'")).ShouldBe(0);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_CrossScopeChild_UnselectedTable'")).ShouldBe(0);
    }

    [Fact]
    public async Task Export_SelectedTableDacpac_IncludesScriptableFunctionDependency()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.FunctionComputed");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);

        using (var model = await LoadStoredDacpacModelAsync(sqlite.FilePath))
        {
            ModelContainsTable(model, "dbo", "FunctionComputed").ShouldBeTrue();
            ModelContainsObject(model, ModelSchema.ScalarFunction, "dbo", "NormalizeCode").ShouldBeTrue();
        }

        var result = await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        result.RowCount.ShouldBe(2);
        (await target.ScalarStringAsync("SELECT STRING_AGG(NormalizedCode, ',') WITHIN GROUP (ORDER BY FunctionComputedId) FROM dbo.FunctionComputed"))
            .ShouldBe("ABC,DEF");
    }

    [Fact]
    public async Task Import_SelectedTableDacpac_DoesNotAlterUnrelatedTargetObjects()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync("""
            CREATE TABLE dbo.UnrelatedTarget (
                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                Name NVARCHAR(50) NOT NULL
            );

            INSERT INTO dbo.UnrelatedTarget (Name) VALUES (N'keep me');
            """);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.SelectedParent");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        await new SqlDataBridgeImporter().ImportAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions { SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac });

        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.UnrelatedTarget")).ShouldBe(1);
        (await target.ScalarStringAsync("SELECT Name FROM dbo.UnrelatedTarget")).ShouldBe("keep me");
    }

    [Fact]
    public async Task ImportPreflight_SelectedTableDacpacRejectsObjectDrops()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = SelectedTableDacpacExportOptions("dbo.SelectedParent");

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().PreflightAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions
            {
                SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
                DacpacDeploymentOptions = new DacpacDeploymentOptions { AllowObjectDrops = true }
            });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("AllowObjectDrops cannot be used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_DefaultDacpac_IncludesUnselectedTables()
    {
        await using var source = await CreateSourceAsync();
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.SelectedParent"],
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            CommandTimeout = 120
        };

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);

        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.ScalarStringAsync("SELECT schema_scope FROM zsb_schema_packages WHERE id = 1"))
            .ShouldBe(nameof(DacpacSchemaScope.Database));

        using var model = await LoadStoredDacpacModelAsync(sqlite.FilePath);
        ModelContainsTable(model, "dbo", "SelectedParent").ShouldBeTrue();
        ModelContainsTable(model, "dbo", "UnselectedTable").ShouldBeTrue();
        ModelContainsTable(model, "tenant", "SelectedThing").ShouldBeTrue();
    }

    [Fact]
    public async Task Import_DatabaseScopeDacpac_AllowsObjectDropsOption()
    {
        await using var source = await CreateSourceAsync();
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync("""
            CREATE TABLE dbo.ExtraTargetOnly (
                Id INT NOT NULL PRIMARY KEY
            );
            """);
        await using var sqlite = new SqliteTempFileHarness();
        var exportOptions = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.SelectedParent"],
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            CommandTimeout = 120
        };

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, exportOptions);
        var result = await new SqlDataBridgeImporter().PreflightAsync(
            sqlite.FilePath,
            target.ConnectionString,
            new ImportOptions
            {
                SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
                DacpacDeploymentOptions = new DacpacDeploymentOptions
                {
                    AllowObjectDrops = true,
                    BlockOnPossibleDataLoss = false
                }
            });

        result.Errors.ShouldNotContain(e => e.Contains("AllowObjectDrops cannot be used", StringComparison.Ordinal));
    }

    private async Task<SqlServerFixtureDatabase> CreateSourceAsync()
    {
        var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("dacpac_scope.sql"));
        await source.ExecuteSqlAsync("""
            CREATE FUNCTION dbo.NormalizeCode(@value NVARCHAR(20))
            RETURNS NVARCHAR(20)
            AS
            BEGIN
                RETURN UPPER(@value);
            END;
            """);
        await source.ExecuteSqlAsync("""
            CREATE TABLE dbo.FunctionComputed (
                FunctionComputedId INT IDENTITY(1,1) NOT NULL,
                RawCode NVARCHAR(20) NOT NULL,
                NormalizedCode AS dbo.NormalizeCode(RawCode),
                CONSTRAINT PK_FunctionComputed PRIMARY KEY CLUSTERED (FunctionComputedId)
            );

            INSERT INTO dbo.FunctionComputed (RawCode)
            VALUES (N'abc'), (N'def');
            """);
        return source;
    }

    private static ExportOptions SelectedTableDacpacExportOptions(params string[] tables)
    {
        return new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = tables,
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            CommandTimeout = 120,
            DacpacCaptureOptions = new DacpacCaptureOptions { SchemaScope = DacpacSchemaScope.SelectedExportTables }
        };
    }

    private static async Task<TSqlModel> LoadStoredDacpacModelAsync(string sqliteFilePath)
    {
        var dacpacPath = Path.Combine(Path.GetTempPath(), $"zsb-test-{Guid.NewGuid():N}.dacpac");
        try
        {
            await using (var sqlite = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqliteFilePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ConnectionString))
            {
                await sqlite.OpenAsync();
                await using var command = sqlite.CreateCommand();
                command.CommandText = "SELECT payload FROM zsb_schema_packages WHERE id = 1";
                var payload = (byte[])(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing dacpac payload."));
                await File.WriteAllBytesAsync(dacpacPath, payload);
            }

            return new TSqlModel(dacpacPath, DacSchemaModelStorageType.Memory);
        }
        finally
        {
            if (File.Exists(dacpacPath))
            {
                File.Delete(dacpacPath);
            }
        }
    }

    private static bool ModelContainsTable(TSqlModel model, string schema, string table)
    {
        return model.GetObject(
            ModelSchema.Table,
            new ObjectIdentifier([schema, table]),
            DacQueryScopes.UserDefined) is not null;
    }

    private static bool ModelContainsObject(TSqlModel model, ModelTypeClass objectType, params string[] parts)
    {
        return model.GetObject(
            objectType,
            new ObjectIdentifier(parts),
            DacQueryScopes.UserDefined) is not null;
    }

    private static bool ModelContainsObjectNamed(TSqlModel model, ModelTypeClass objectType, string name)
    {
        return model
            .GetObjects(DacQueryScopes.UserDefined, objectType)
            .Any(o => o.Name.Parts.Any(part => string.Equals(part, name, StringComparison.OrdinalIgnoreCase)));
    }
}
