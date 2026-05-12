using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class Phase0HarnessTests
{
    private readonly SqlServerContainerFixture _fixture;

    public Phase0HarnessTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BasicTypesFixture_AppliesAndSeedsRows()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("basic_types.sql");

        await db.ExecuteSqlAsync(sql);

        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Customers")).ShouldBe(10);
        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Orders")).ShouldBe(12);
        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Documents")).ShouldBe(10);
    }

    [Fact]
    public async Task IdentityFkFixture_AppliesAndMaintainsReferences()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("identity_fk.sql");

        await db.ExecuteSqlAsync(sql);

        var orphanCount = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM dbo.Child c
            LEFT JOIN dbo.Parent p ON p.ParentId = c.ParentId
            WHERE p.ParentId IS NULL");

        orphanCount.ShouldBe(0);
    }


    [Fact]
    public async Task ExclusionsFixture_ProvidesSkippedTableAndColumnTargets()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("exclusions.sql");

        await db.ExecuteSqlAsync(sql);

        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe")).ShouldBe(3);
        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.SkipMe")).ShouldBe(2);

        var hasSkipCol = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'IncludeMe'
              AND COLUMN_NAME = 'SkipCol'");

        hasSkipCol.ShouldBe(1);
    }
    [Fact]
    public async Task UnsupportedTypesFixture_ContainsSqlVariant()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("unsupported_types.sql");

        await db.ExecuteSqlAsync(sql);

        var unsupportedCount = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'UnsupportedPayloads'
              AND DATA_TYPE = 'sql_variant'");

        unsupportedCount.ShouldBe(1);
    }

    [Fact]
    public async Task XmlPayloadsFixture_ContainsXmlColumnAndRows()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("xml_payloads.sql");

        await db.ExecuteSqlAsync(sql);

        var xmlColumnCount = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'XmlPayloads'
              AND COLUMN_NAME = 'PayloadXml'
              AND DATA_TYPE = 'xml'");

        xmlColumnCount.ShouldBe(1);
        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.XmlPayloads")).ShouldBe(4);
    }

    [Fact]
    public async Task JsonPayloadsFixture_ContainsNativeJsonColumnAndRowsOnSqlServer2025()
    {
        if (!_fixture.SupportsNativeJson)
        {
            return;
        }

        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("json_payloads.sql");

        await db.ExecuteSqlAsync(sql);

        var jsonColumnCount = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = 'JsonPayloads'
              AND COLUMN_NAME = 'PayloadJson'
              AND DATA_TYPE = 'json'");

        jsonColumnCount.ShouldBe(1);
        (await db.ScalarIntAsync("SELECT COUNT(*) FROM dbo.JsonPayloads")).ShouldBe(3);
    }

    [Fact]
    public async Task ComputedColumnFixture_MarksComputedColumnInCatalog()
    {
        await using var db = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        var sql = SqlScriptLoader.LoadEmbeddedScript("computed_column.sql");

        await db.ExecuteSqlAsync(sql);

        var computedCount = await db.ScalarIntAsync(@"
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.InvoiceLines')
              AND name = 'ExtendedPrice'
              AND is_computed = 1");

        computedCount.ShouldBe(1);
    }

    [Fact]
    public async Task SqliteTempFileHarness_CreatesWritableDatabase()
    {
        await using var sqlite = new SqliteTempFileHarness();
        await using var connection = await sqlite.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE probe (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO probe(name) VALUES ('ok');";

        await command.ExecuteNonQueryAsync();

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM probe";
        var result = await countCommand.ExecuteScalarAsync();

        Convert.ToInt32(result).ShouldBe(1);
        File.Exists(sqlite.FilePath).ShouldBeTrue();
    }
}
