using System.Globalization;
using System.Data.SqlTypes;
using Microsoft.Data.SqlTypes;
using System.Xml;
using Zachtbeer.SqlDataBridge.Internal;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class ValueConverterTests
{
    [Theory]
    [InlineData("bigint", "INTEGER")]
    [InlineData("int", "INTEGER")]
    [InlineData("smallint", "INTEGER")]
    [InlineData("tinyint", "INTEGER")]
    [InlineData("bit", "INTEGER")]
    [InlineData("float", "REAL")]
    [InlineData("real", "REAL")]
    [InlineData("char", "TEXT")]
    [InlineData("varchar", "TEXT")]
    [InlineData("nchar", "TEXT")]
    [InlineData("nvarchar", "TEXT")]
    [InlineData("decimal", "TEXT")]
    [InlineData("numeric", "TEXT")]
    [InlineData("money", "TEXT")]
    [InlineData("smallmoney", "TEXT")]
    [InlineData("date", "TEXT")]
    [InlineData("datetime", "TEXT")]
    [InlineData("datetime2", "TEXT")]
    [InlineData("datetimeoffset", "TEXT")]
    [InlineData("time", "TEXT")]
    [InlineData("uniqueidentifier", "TEXT")]
    [InlineData("xml", "TEXT")]
    [InlineData("json", "TEXT")]
    [InlineData("binary", "BLOB")]
    [InlineData("varbinary", "BLOB")]
    public void SqliteTypeFor_SupportedTypes_ReturnsExpectedStorageType(string sqlServerType, string expectedSqliteType)
    {
        var column = Column(sqlServerType);

        ValueConverter.SqliteTypeFor(column).ShouldBe(expectedSqliteType);
    }

    [Theory]
    [InlineData("sql_variant")]
    [InlineData("timestamp")]
    [InlineData("rowversion")]
    [InlineData("geography")]
    [InlineData("geometry")]
    [InlineData("hierarchyid")]
    public void IsUnsupported_UnsupportedTypes_ReturnsTrue(string sqlServerType)
    {
        ValueConverter.IsUnsupported(sqlServerType).ShouldBeTrue();
    }

    [Theory]
    [InlineData("sql_variant")]
    [InlineData("geography")]
    public void SqliteTypeFor_UnsupportedTypes_Throws(string sqlServerType)
    {
        var exception = Should.Throw<BridgeException>(() => ValueConverter.SqliteTypeFor(Column(sqlServerType)));

        exception.Message.ShouldContain($"Unsupported SQL Server type '{sqlServerType}'");
        exception.Message.ShouldContain("dbo.Sample.Value");
    }

    [Fact]
    public void ToSqliteValue_DecimalAndMoneyTypes_UseInvariantText()
    {
        ValueConverter.ToSqliteValue(1234.56m, Column("decimal")).ShouldBe("1234.56");
        ValueConverter.ToSqliteValue(9876.54m, Column("money")).ShouldBe("9876.54");
    }

    [Fact]
    public void ToSqliteValue_UniqueIdentifier_UsesCanonicalText()
    {
        var guid = Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF");

        ValueConverter.ToSqliteValue(guid, Column("uniqueidentifier")).ShouldBe("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    }

    [Fact]
    public void Bit_RoundTripsThroughIntegerRepresentation()
    {
        var column = Column("bit");

        ValueConverter.ToSqliteValue(true, column).ShouldBe(1);
        ValueConverter.ToSqliteValue(false, column).ShouldBe(0);
        ValueConverter.FromSqliteValue(1L, column).ShouldBe(true);
        ValueConverter.FromSqliteValue(0L, column).ShouldBe(false);
    }

    [Fact]
    public void DateTime_ToSqliteValue_UsesRoundTripText()
    {
        var value = new DateTime(2024, 2, 3, 4, 5, 6, 789, DateTimeKind.Utc);
        var sqliteValue = ValueConverter.ToSqliteValue(value, Column("datetime2")).ShouldBeOfType<string>();

        sqliteValue.ShouldBe(value.ToString("O", CultureInfo.InvariantCulture));
        ValueConverter.FromSqliteValue(sqliteValue, Column("datetime2")).ShouldBe(value);
    }

    [Fact]
    public void Xml_RoundTripsThroughTextRepresentation()
    {
        const string xml = """<root><value id="1">alpha</value></root>""";
        using var reader = XmlReader.Create(new StringReader(xml));
        var sqliteValue = ValueConverter.ToSqliteValue(new SqlXml(reader), Column("xml"));

        sqliteValue.ShouldBe(xml);
        ValueConverter.FromSqliteValue(sqliteValue, Column("xml")).ShouldBe(xml);
    }

    [Fact]
    public void Json_RoundTripsThroughTextRepresentation()
    {
        const string json = """{"id":1,"name":"alpha","tags":["one","two"]}""";

        ValueConverter.ToSqliteValue(new SqlJson(json), Column("json")).ShouldBe(json);
        ValueConverter.ToSqliteValue(json, Column("json")).ShouldBe(json);
        ValueConverter.FromSqliteValue(json, Column("json")).ShouldBe(json);
    }

    private static ColumnMetadata Column(string sqlServerType)
    {
        return new ColumnMetadata(
            new TableName("dbo", "Sample"),
            "Value",
            1,
            sqlServerType,
            0,
            0,
            0,
            true,
            false,
            false,
            null,
            false);
    }
}
