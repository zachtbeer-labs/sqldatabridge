using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class ExportPackageTests
{
    private readonly SqlServerContainerFixture _fixture;

    public ExportPackageTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Export_BasicTypes_WritesCompletePackageMetadataAndRows()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(32);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasRequiredMetadataTablesAsync(connection);
        await SqlitePackageAssertions.HasRunMetadataAsync(connection);
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.Customers", "dbo.Documents", "dbo.Orders");
        await SqlitePackageAssertions.HasImportPlanAsync(connection, "dbo.Customers", "dbo.Documents", "dbo.Orders");
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.Customers", 10);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.Documents", 10);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.Orders", 12);

        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_columns")).ShouldBe(14);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_warnings")).ShouldBe(0);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__customers")).ShouldBe(10);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__orders")).ShouldBe(12);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__documents")).ShouldBe(10);
    }

    [Fact]
    public async Task Export_CustomDataTablePrefix_WritesConfiguredSqliteTableNames()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.Customers"],
            DataTablePrefix = "custom_data"
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(10);

        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.TableExistsAsync("custom_data_dbo__customers")).ShouldBeTrue();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM custom_data_dbo__customers")).ShouldBe(10);
        (await connection.ScalarStringAsync("SELECT sqlite_table FROM zsb_tables WHERE source_schema = 'dbo' AND source_table = 'Customers'"))
            .ShouldBe("custom_data_dbo__customers");
    }

    [Fact]
    public async Task Export_EmptyDataTablePrefix_PreservesSourceTableLeadingUnderscores()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("underscore_table_names.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { DataTablePrefix = "" };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);

        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.TableExistsAsync("dbo____accountsbackup")).ShouldBeTrue();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM dbo____accountsbackup")).ShouldBe(2);
        (await connection.ScalarStringAsync("SELECT sqlite_table FROM zsb_tables WHERE source_schema = 'dbo' AND source_table = '__AccountsBackup'"))
            .ShouldBe("dbo____accountsbackup");
    }

    [Fact]
    public async Task Export_ExistingPackageWithoutOverwrite_FailsWithoutReplacingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        await File.WriteAllTextAsync(sqlite.FilePath, "existing package");

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath));

        exception.Message.ShouldContain("already exists");
        (await File.ReadAllTextAsync(sqlite.FilePath)).ShouldBe("existing package");
    }

    [Fact]
    public async Task Export_OverwriteExistingPackage_ReplacesPackageAfterSuccess()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        await File.WriteAllTextAsync(sqlite.FilePath, "old package");
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.IncludeMe"],
            OverwriteExistingPackage = true
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);
        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasRunMetadataAsync(connection);
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.IncludeMe");
    }

    [Fact]
    public async Task Export_OverwriteExistingPackage_DoesNotReplaceWhenPreflightFails()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("unsupported_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        await File.WriteAllTextAsync(sqlite.FilePath, "existing package");
        var options = new ExportOptions { OverwriteExistingPackage = true };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Unsupported included type");
        (await File.ReadAllTextAsync(sqlite.FilePath)).ShouldBe("existing package");
    }

    [Theory]
    [InlineData("Customers")]
    [InlineData("dbo.Customers")]
    [InlineData("dbo.Cust*")]
    public async Task Export_OnlyTablePatterns_SelectExpectedTable(string includePattern)
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = [includePattern]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(10);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.Customers");
        await SqlitePackageAssertions.HasImportPlanAsync(connection, "dbo.Customers");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__customers")).ShouldBe(10);
    }

    [Fact]
    public async Task Export_OnlyTablePatterns_SelectMultipleTablesAndPreserveImportOrder()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.Customers", "dbo.Orders"]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(2);
        result.RowCount.ShouldBe(22);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.Customers", "dbo.Orders");
        await SqlitePackageAssertions.HasImportPlanAsync(connection, "dbo.Customers", "dbo.Orders");
        (await connection.TableExistsAsync("zsb_data_dbo__documents")).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_OnlyWithoutTablePatterns_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { TableSelection = ExportTableSelectionMode.Only };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("TableSelection Only requires at least one table pattern");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ExportTableSelectionMode.AllExcept)]
    [InlineData(ExportTableSelectionMode.Only)]
    public async Task Export_UnmatchedTablePattern_FailsBeforeCreatingPackage(ExportTableSelectionMode mode)
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = mode,
            Tables = ["dbo.DoesNotExist"]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Table pattern 'dbo.DoesNotExist' did not match any user table");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_AllExceptTable_RemovesTableAndRecordsSkippedTable()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { Tables = ["dbo.SkipMe"] };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.IncludeMe");
        await SqlitePackageAssertions.HasExclusionAsync(connection, "table", "dbo.SkipMe");
        (await connection.TableExistsAsync("zsb_data_dbo__skipme")).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_AllExceptWildcard_RemovesMatchingTablesAndRecordsSkippedTables()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { Tables = ["dbo.Tenant*"] };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.GlobalSettings");
        await SqlitePackageAssertions.HasExclusionAsync(connection, "table", "dbo.TenantCustomers");
        await SqlitePackageAssertions.HasExclusionAsync(connection, "table", "dbo.TenantOrders");
        (await connection.TableExistsAsync("zsb_data_dbo__tenantcustomers")).ShouldBeFalse();
        (await connection.TableExistsAsync("zsb_data_dbo__tenantorders")).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_AllExceptAllTables_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("basic_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions { Tables = ["dbo.*"] };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("No tables are selected for export");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_ExcludeColumn_RemovesColumnAndRecordsSkippedColumn()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.IncludeMe"],
            ExcludeColumns = ["dbo.IncludeMe.SkipCol"]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExclusionAsync(connection, "column", "dbo.IncludeMe.SkipCol");
        (await connection.TableColumnExistsAsync("zsb_data_dbo__includeme", "KeepCol")).ShouldBeTrue();
        (await connection.TableColumnExistsAsync("zsb_data_dbo__includeme", "SkipCol")).ShouldBeFalse();
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_columns c
            INNER JOIN zsb_tables t ON t.id = c.table_id
            WHERE t.source_schema = 'dbo'
              AND t.source_table = 'IncludeMe'
              AND c.column_name = 'SkipCol'
              AND c.is_excluded = 1
            """)).ShouldBe(1);
    }

    [Fact]
    public async Task Export_InvalidColumnExclusion_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.IncludeMe"],
            ExcludeColumns = ["dbo.IncludeMe.DoesNotExist"]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Excluded column 'dbo.IncludeMe.DoesNotExist' does not exist");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_UnsupportedIncludedType_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("unsupported_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath));

        exception.Message.ShouldContain("Unsupported included type");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_UnsupportedExcludedColumns_SucceedsAndRecordsExclusions()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("unsupported_types.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            ExcludeColumns =
            [
                "dbo.UnsupportedPayloads.PayloadVariant"
            ]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(2);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExclusionAsync(connection, "column", "dbo.UnsupportedPayloads.PayloadVariant");
        (await connection.TableColumnExistsAsync("zsb_data_dbo__unsupportedpayloads", "PayloadVariant")).ShouldBeFalse();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__unsupportedpayloads")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_XmlColumn_WritesTextStorageAndXmlMetadata()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("xml_payloads.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(4);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.XmlPayloads");
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_columns c
            INNER JOIN zsb_tables t ON t.id = c.table_id
            WHERE t.source_schema = 'dbo'
              AND t.source_table = 'XmlPayloads'
              AND c.column_name = 'PayloadXml'
              AND c.sql_server_type_name = 'xml'
            """)).ShouldBe(1);
        (await connection.ScalarStringAsync("SELECT type FROM pragma_table_info('zsb_data_dbo__xmlpayloads') WHERE name = 'PayloadXml'")).ShouldBe("TEXT");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__xmlpayloads WHERE PayloadName = 'element-attribute' AND PayloadXml LIKE '%<item id=\"1\">alpha</item>%'")).ShouldBe(1);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__xmlpayloads WHERE PayloadName = 'null-payload' AND PayloadXml IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Export_NativeJsonColumn_WritesTextStorageAndJsonMetadataOnSqlServer2025()
    {
        if (!_fixture.SupportsNativeJson)
        {
            return;
        }

        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("json_payloads.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.JsonPayloads");
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_columns c
            INNER JOIN zsb_tables t ON t.id = c.table_id
            WHERE t.source_schema = 'dbo'
              AND t.source_table = 'JsonPayloads'
              AND c.column_name = 'PayloadJson'
              AND c.sql_server_type_name = 'json'
            """)).ShouldBe(1);
        (await connection.ScalarStringAsync("SELECT type FROM pragma_table_info('zsb_data_dbo__jsonpayloads') WHERE name = 'PayloadJson'")).ShouldBe("TEXT");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__jsonpayloads WHERE PayloadName = 'object-array' AND PayloadJson LIKE '%\"tags\":[\"one\",\"two\"]%'")).ShouldBe(1);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__jsonpayloads WHERE PayloadName = 'null-payload' AND PayloadJson IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task Export_GlobalWhereClauses_FilterMatchingTablesAndRowCounts()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            GlobalWhereClauses =
            [
                new GlobalWhereClause("TenantId", "TenantId = 123"),
                new GlobalWhereClause("Active", "Active = 1")
            ]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(5);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 1);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 2);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantcustomers")).ShouldBe(1);
        (await connection.ScalarStringAsync("SELECT Name FROM zsb_data_dbo__tenantcustomers")).ShouldBe("Alice");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(2);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__globalsettings")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_OnlyWithGlobalAndPerTableWhereClauses_FiltersSelectedTablesOnly()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.Tenant*"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantCustomers", "Active = 1")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(2);
        result.RowCount.ShouldBe(3);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.TenantCustomers", "dbo.TenantOrders");
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 1);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 2);
        (await connection.TableExistsAsync("zsb_data_dbo__globalsettings")).ShouldBeFalse();
        (await connection.ScalarStringAsync("SELECT Name FROM zsb_data_dbo__tenantcustomers")).ShouldBe("Alice");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_GlobalWhereClauseCanUseExcludedColumn()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.TenantOrders"],
            ExcludeColumns = ["dbo.TenantOrders.TenantId"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.RowCount.ShouldBe(2);
        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.TableColumnExistsAsync("zsb_data_dbo__tenantorders", "TenantId")).ShouldBeFalse();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_GlobalWhereClauseWithEmptyColumnName_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            GlobalWhereClauses = [new GlobalWhereClause("", "TenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Global WHERE clause column name cannot be empty");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_GlobalWhereClauseWithEmptyPredicate_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Global WHERE clause for column 'TenantId' cannot be empty");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_GlobalWhereClauseExcludedByAllExceptScope_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            Tables = ["dbo.Tenant*"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Global WHERE clause column 'TenantId' did not match any selected source table");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_AllExceptWithGlobalWhereClause_FiltersRemainingMatchingTables()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            Tables = ["dbo.TenantOrders"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(2);
        result.RowCount.ShouldBe(4);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.TenantCustomers", "dbo.GlobalSettings");
        await SqlitePackageAssertions.HasExclusionAsync(connection, "table", "dbo.TenantOrders");
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 2);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.TableExistsAsync("zsb_data_dbo__tenantorders")).ShouldBeFalse();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantcustomers")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_PerTableWhereClauses_StackWithGlobalWhereClauses()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantOrders", "Amount >= 20.00")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(5);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 2);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 1);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantcustomers")).ShouldBe(2);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(1);
        (await connection.ScalarStringAsync("SELECT Amount FROM zsb_data_dbo__tenantorders")).ShouldBe("20.00");
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__globalsettings")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_MultiplePerTableWhereClausesForSameTable_StackWithAnd()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            PerTableWhereClauses =
            [
                new PerTableWhereClause("dbo.TenantOrders", "TenantId = 123"),
                new PerTableWhereClause("dbo.TenantOrders", "Amount < 20.00")
            ]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(6);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 3);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 1);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.ScalarStringAsync("SELECT Amount FROM zsb_data_dbo__tenantorders")).ShouldBe("10.00");
    }

    [Fact]
    public async Task Export_PerTableWhereClauseTableName_IsTrimmedAndCaseInsensitive()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            PerTableWhereClauses = [new PerTableWhereClause(" dbo.tenantorders ", "TenantId = 456")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(6);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 3);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 1);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.ScalarStringAsync("SELECT Amount FROM zsb_data_dbo__tenantorders")).ShouldBe("30.00");
    }

    [Fact]
    public async Task Export_PerTableWhereClause_AppliesOnlyToExactTargetTable()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantCustomers", "TenantId = 123")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.TableCount.ShouldBe(3);
        result.RowCount.ShouldBe(7);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantCustomers", 2);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TenantOrders", 3);
        await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.GlobalSettings", 2);
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(3);
    }

    [Fact]
    public async Task Export_PerTableWhereClauseCanUseExcludedColumn()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.TenantOrders"],
            ExcludeColumns = ["dbo.TenantOrders.TenantId"],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantOrders", "TenantId = 123")]
        };

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options);

        result.RowCount.ShouldBe(2);
        await using var connection = await sqlite.OpenConnectionAsync();
        (await connection.TableColumnExistsAsync("zsb_data_dbo__tenantorders", "TenantId")).ShouldBeFalse();
        (await connection.ScalarIntAsync("SELECT COUNT(*) FROM zsb_data_dbo__tenantorders")).ShouldBe(2);
    }

    [Fact]
    public async Task Export_AllExceptExcludingPerTableWhereClauseTarget_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            Tables = ["dbo.TenantOrders"],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantOrders", "TenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Per-table WHERE clause table 'dbo.TenantOrders' is not in the selected export scope");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_PerTableWhereClauseWithEmptyTableName_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            PerTableWhereClauses = [new PerTableWhereClause("", "TenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Per-table WHERE clause table name cannot be empty");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_PerTableWhereClauseWithEmptyPredicate_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantOrders", "")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Per-table WHERE clause for table 'dbo.TenantOrders' cannot be empty");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_PerTableWhereClauseOutsideSelectedScope_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.TenantCustomers"],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.TenantOrders", "TenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Per-table WHERE clause table 'dbo.TenantOrders' is not in the selected export scope");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_GlobalWhereClauseWithoutMatchingColumn_FailsBeforeCreatingPackage()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("global_where_clauses.sql"));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            GlobalWhereClauses = [new GlobalWhereClause("MissingTenantId", "MissingTenantId = 123")]
        };

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath, options));

        exception.Message.ShouldContain("Global WHERE clause column 'MissingTenantId' did not match any selected source table");
        File.Exists(sqlite.FilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_ComputedColumn_RecordsSkippedColumnAndOmitsDataColumn()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await db.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("computed_column.sql"));
        await using var sqlite = new SqliteTempFileHarness();

        var result = await new SqlDataBridgeExporter().ExportAsync(db.ConnectionString, sqlite.FilePath);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);

        await using var connection = await sqlite.OpenConnectionAsync();
        await SqlitePackageAssertions.HasExclusionAsync(connection, "column", "dbo.InvoiceLines.ExtendedPrice");
        (await connection.TableColumnExistsAsync("zsb_data_dbo__invoicelines", "ExtendedPrice")).ShouldBeFalse();
        (await connection.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM zsb_columns
            WHERE column_name = 'ExtendedPrice'
              AND is_computed = 1
            """)).ShouldBe(1);
    }
}
