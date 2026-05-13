using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class RoundTripTests
{
    private readonly SqlServerContainerFixture _fixture;

    public RoundTripTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RoundTrip_ComputedColumn_SkipsComputedColumn()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("computed_column.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.ComputedInvoiceLines());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.InvoiceLines WHERE ExtendedPrice = Qty * UnitPrice")).ShouldBe(3);
    }

    [Fact]
    public async Task RoundTrip_IdentityForeignKeys_PreservesRowsAndReferences()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("identity_fk.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IdentityForeignKeys());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.TableCount.ShouldBe(2);
        result.RowCount.ShouldBe(7);
        (await target.ScalarStringAsync("SELECT STRING_AGG(CONVERT(varchar(10), ParentId), ',') WITHIN GROUP (ORDER BY ParentId) FROM dbo.Parent")).ShouldBe("1,2,3");
        (await target.ScalarStringAsync("SELECT STRING_AGG(CONVERT(varchar(10), ChildId), ',') WITHIN GROUP (ORDER BY ChildId) FROM dbo.Child")).ShouldBe("1,2,3,4");
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.Child c
            LEFT JOIN dbo.Parent p ON p.ParentId = c.ParentId
            WHERE p.ParentId IS NULL
            """)).ShouldBe(0);
    }

    [Fact]
    public async Task RoundTrip_TypeFidelity_PreservesMetadataStorageAndValues()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("type_fidelity.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.TypeSamples());
        await using var sqlite = new SqliteTempFileHarness();

        var exportResult = await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);

        exportResult.TableCount.ShouldBe(1);
        exportResult.RowCount.ShouldBe(2);

        await using (var connection = await sqlite.OpenConnectionAsync())
        {
            await SqlitePackageAssertions.HasExportedTablesAsync(connection, "dbo.TypeSamples");
            await SqlitePackageAssertions.HasTableRowCountAsync(connection, "dbo.TypeSamples", 2);
            (await connection.ScalarIntAsync("""
                SELECT COUNT(*)
                FROM zsb_columns c
                INNER JOIN zsb_tables t ON t.id = c.table_id
                WHERE t.source_schema = 'dbo'
                  AND t.source_table = 'TypeSamples'
                  AND c.column_name = 'NumericValue'
                  AND c.sql_server_type_name = 'numeric'
                  AND c.precision_value = 12
                  AND c.scale_value = 4
                """)).ShouldBe(1);
            (await connection.ScalarIntAsync("""
                SELECT COUNT(*)
                FROM zsb_columns c
                INNER JOIN zsb_tables t ON t.id = c.table_id
                WHERE t.source_schema = 'dbo'
                  AND t.source_table = 'TypeSamples'
                  AND c.column_name = 'DecimalValue'
                  AND c.sql_server_type_name = 'decimal'
                  AND c.precision_value = 18
                  AND c.scale_value = 6
                """)).ShouldBe(1);
            (await connection.ScalarIntAsync("""
                SELECT COUNT(*)
                FROM zsb_columns c
                INNER JOIN zsb_tables t ON t.id = c.table_id
                WHERE t.source_schema = 'dbo'
                  AND t.source_table = 'TypeSamples'
                  AND c.column_name = 'NVarCharValue'
                  AND c.max_length = 40
                  AND c.is_nullable = 0
                """)).ShouldBe(1);
            (await connection.ScalarIntAsync("""
                SELECT COUNT(*)
                FROM zsb_columns c
                INNER JOIN zsb_tables t ON t.id = c.table_id
                WHERE t.source_schema = 'dbo'
                  AND t.source_table = 'TypeSamples'
                  AND c.column_name = 'NullableText'
                  AND c.is_nullable = 1
                """)).ShouldBe(1);

            (await connection.ScalarIntAsync("SELECT COUNT(*) FROM pragma_table_info('zsb_data_dbo__typesamples') WHERE type = 'INTEGER'")).ShouldBe(6);
            (await connection.ScalarIntAsync("SELECT COUNT(*) FROM pragma_table_info('zsb_data_dbo__typesamples') WHERE type = 'REAL'")).ShouldBe(2);
            (await connection.ScalarIntAsync("SELECT COUNT(*) FROM pragma_table_info('zsb_data_dbo__typesamples') WHERE type = 'TEXT'")).ShouldBe(16);
            (await connection.ScalarIntAsync("SELECT COUNT(*) FROM pragma_table_info('zsb_data_dbo__typesamples') WHERE type = 'BLOB'")).ShouldBe(1);

            (await connection.ScalarStringAsync("SELECT typeof(NumericValue) FROM zsb_data_dbo__typesamples WHERE Id = 1")).ShouldBe("text");
            (await connection.ScalarStringAsync("SELECT typeof(MoneyValue) FROM zsb_data_dbo__typesamples WHERE Id = 1")).ShouldBe("text");
            (await connection.ScalarStringAsync("SELECT DateValue FROM zsb_data_dbo__typesamples WHERE Id = 1")).ShouldBe("2024-03-04T00:00:00.0000000");
            (await connection.ScalarStringAsync("SELECT GuidValue FROM zsb_data_dbo__typesamples WHERE Id = 1")).ShouldBe("6f9619ff-8b86-d011-b42d-00c04fc964ff");
            (await connection.ScalarStringAsync("SELECT hex(BlobValue) FROM zsb_data_dbo__typesamples WHERE Id = 1")).ShouldBe("01020304");
        }

        var importResult = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        importResult.TableCount.ShouldBe(1);
        importResult.RowCount.ShouldBe(2);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.TypeSamples
            WHERE Id = 1
              AND TinyValue = 250
              AND SmallValue = -1234
              AND IntValue = 123456
              AND BigValue = 9876543210
              AND ABS(CAST(RealValue AS FLOAT) - 1.5) < 0.0001
              AND ABS(FloatValue - 2.25) < 0.0001
              AND CharValue = 'ABC'
              AND VarCharValue = 'varchar-one'
              AND NCharValue = N'UNO'
              AND NVarCharValue = N'nvarchar-one'
              AND NumericValue = CAST(12345.6789 AS NUMERIC(12,4))
              AND DecimalValue = CAST(987654321.123456 AS DECIMAL(18,6))
              AND MoneyValue = CAST(1234.5678 AS MONEY)
              AND SmallMoneyValue = CAST(12.3456 AS SMALLMONEY)
              AND DateValue = CAST('2024-03-04' AS DATE)
              AND DateTimeValue = CAST('2024-03-04T05:06:07.123' AS DATETIME)
              AND DateTime2Value = CAST('2024-03-04T05:06:07.1234567' AS DATETIME2(7))
              AND DateTimeOffsetValue = CAST('2024-03-04T05:06:07.890-07:00' AS DATETIMEOFFSET(3))
              AND TimeValue = CAST('12:34:56.7891' AS TIME(4))
              AND GuidValue = CAST('6f9619ff-8b86-d011-b42d-00c04fc964ff' AS UNIQUEIDENTIFIER)
              AND BlobValue = 0x01020304
              AND NullableText = N'nullable text'
              AND NullableInt = 42
              AND NullableDate = CAST('2024-04-05T06:07:08.123' AS DATETIME2(3))
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.TypeSamples
            WHERE Id = 2
              AND TinyValue = 1
              AND SmallValue = 2
              AND IntValue = 3
              AND BigValue = 4
              AND ABS(CAST(RealValue AS FLOAT) - 5.5) < 0.0001
              AND ABS(FloatValue - 6.25) < 0.0001
              AND NumericValue = CAST(7.8901 AS NUMERIC(12,4))
              AND DecimalValue = CAST(8.900000 AS DECIMAL(18,6))
              AND MoneyValue = CAST(9.1000 AS MONEY)
              AND SmallMoneyValue = CAST(10.2000 AS SMALLMONEY)
              AND DateValue = CAST('2025-01-02' AS DATE)
              AND DateTimeValue = CAST('2025-01-02T03:04:05.127' AS DATETIME)
              AND DateTime2Value = CAST('2025-01-02T03:04:05.9876543' AS DATETIME2(7))
              AND DateTimeOffsetValue = CAST('2025-01-02T03:04:05.432+02:30' AS DATETIMEOFFSET(3))
              AND TimeValue = CAST('01:02:03.4567' AS TIME(4))
              AND GuidValue = CAST('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' AS UNIQUEIDENTIFIER)
              AND BlobValue = 0x0A0B0C0D
              AND NullableText IS NULL
              AND NullableInt IS NULL
              AND NullableDate IS NULL
            """)).ShouldBe(1);
    }

    [Fact]
    public async Task RoundTrip_ExtraNullableAndDefaultedTargetColumns_AreAllowed()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("exclusions.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.IncludeMe(nullableExtra: true, defaultedExtra: true));
        await using var sqlite = new SqliteTempFileHarness();
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.IncludeMe"],
            ExcludeColumns = ["dbo.IncludeMe.SkipCol"]
        };

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath, options);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.IncludeMe WHERE NullableExtra IS NULL AND DefaultedExtra = 42")).ShouldBe(3);
    }

    [Fact]
    public async Task RoundTrip_XmlColumn_PreservesXmlValuesAndNulls()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("xml_payloads.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.XmlPayloads());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(4);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.XmlPayloads
            WHERE PayloadName = N'element-attribute'
              AND PayloadXml.exist('/root/item[@id="1" and text()[1] = "alpha"]') = 1
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.XmlPayloads
            WHERE PayloadName = N'namespaced'
              AND PayloadXml.exist('declare namespace ns="urn:test"; /ns:root/ns:item[@name="beta" and text()[1] = "value"]') = 1
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.XmlPayloads
            WHERE PayloadName = N'mixed-content'
              AND PayloadXml.value('(/root/text()[1])[1]', 'nvarchar(20)') = N'leading '
              AND PayloadXml.value('(/root/text()[2])[1]', 'nvarchar(20)') = N' trailing'
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.XmlPayloads WHERE PayloadName = N'null-payload' AND PayloadXml IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task RoundTrip_NativeJsonColumn_PreservesJsonValuesAndNullsOnSqlServer2025()
    {
        if (!_fixture.SupportsNativeJson)
        {
            return;
        }

        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await source.ExecuteSqlAsync(SqlScriptLoader.LoadEmbeddedScript("json_payloads.sql"));
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await target.ExecuteSqlAsync(TargetSchemaScripts.JsonPayloads());
        await using var sqlite = new SqliteTempFileHarness();

        await new SqlDataBridgeExporter().ExportAsync(source.ConnectionString, sqlite.FilePath);
        var result = await new SqlDataBridgeImporter().ImportAsync(sqlite.FilePath, target.ConnectionString);

        result.TableCount.ShouldBe(1);
        result.RowCount.ShouldBe(3);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.JsonPayloads
            WHERE PayloadName = N'object-array'
              AND JSON_VALUE(PayloadJson, '$.name') = N'alpha'
              AND JSON_VALUE(PayloadJson, '$.tags[1]') = N'two'
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM dbo.JsonPayloads
            WHERE PayloadName = N'nested-object'
              AND JSON_VALUE(PayloadJson, '$.profile.active') = N'true'
              AND JSON_VALUE(PayloadJson, '$.profile.score') = N'12.5'
            """)).ShouldBe(1);
        (await target.ScalarIntAsync("SELECT COUNT(*) FROM dbo.JsonPayloads WHERE PayloadName = N'null-payload' AND PayloadJson IS NULL")).ShouldBe(1);
    }
}
