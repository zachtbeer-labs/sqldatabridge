using Zachtbeer.SqlDataBridge.Internal;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class BridgeIdentifierTests
{
    [Theory]
    [InlineData(null, "dbo__customers")]
    [InlineData("", "dbo__customers")]
    [InlineData("   ", "dbo__customers")]
    [InlineData("zsb_data", "zsb_data_dbo__customers")]
    [InlineData("custom", "custom_dbo__customers")]
    public void ToSqliteDataTableName_UsesConfiguredDataTablePrefix(string? prefix, string expected)
    {
        var table = new TableName("dbo", "Customers");

        BridgeIdentifier.ToSqliteDataTableName(table, prefix).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "dbo____accountsbackup")]
    [InlineData("", "dbo____accountsbackup")]
    [InlineData("zsb_data", "zsb_data_dbo____accountsbackup")]
    public void ToSqliteDataTableName_PreservesSanitizedLeadingUnderscores(string? prefix, string expected)
    {
        var table = new TableName("dbo", "__AccountsBackup");

        BridgeIdentifier.ToSqliteDataTableName(table, prefix).ShouldBe(expected);
    }

    [Theory]
    [InlineData("zsb-data")]
    [InlineData("zsb data")]
    [InlineData("zsb.data")]
    public void ToSqliteDataTableName_InvalidPrefix_Throws(string prefix)
    {
        var table = new TableName("dbo", "Customers");

        var exception = Should.Throw<BridgeException>(() => BridgeIdentifier.ToSqliteDataTableName(table, prefix));

        exception.Message.ShouldContain("DataTablePrefix");
    }

    [Theory]
    [InlineData("dbo.Customers", true)]
    [InlineData("Customers", true)]
    [InlineData("dbo.Orders", false)]
    [InlineData("Orders", false)]
    public void MatchesPattern_ExactNames_MatchesSchemaQualifiedOrTableName(string pattern, bool expected)
    {
        var table = new TableName("dbo", "Customers");

        BridgeIdentifier.MatchesPattern(table, pattern).ShouldBe(expected);
    }

    [Theory]
    [InlineData("dbo.Cust*", true)]
    [InlineData("dbo.*", true)]
    [InlineData("*Customers", true)]
    [InlineData("sales.*", false)]
    [InlineData("*.Orders", false)]
    public void MatchesPattern_Wildcards_MatchFullNameCaseInsensitively(string pattern, bool expected)
    {
        var table = new TableName("dbo", "Customers");

        BridgeIdentifier.MatchesPattern(table, pattern).ShouldBe(expected);
    }

    [Fact]
    public void MatchesPattern_IsCaseInsensitive()
    {
        var table = new TableName("dbo", "Customers");

        BridgeIdentifier.MatchesPattern(table, "DBO.CUSTOMERS").ShouldBeTrue();
        BridgeIdentifier.MatchesPattern(table, "CUSTOMERS").ShouldBeTrue();
        BridgeIdentifier.MatchesPattern(table, "DBO.CUST*").ShouldBeTrue();
    }

    [Fact]
    public void ParseColumnPath_ValidPath_ReturnsParts()
    {
        var result = BridgeIdentifier.ParseColumnPath("dbo.Customers.Name");

        result.Schema.ShouldBe("dbo");
        result.Table.ShouldBe("Customers");
        result.Column.ShouldBe("Name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("dbo")]
    [InlineData("dbo.Customers")]
    [InlineData("dbo.Customers.Name.Extra")]
    [InlineData("dbo..Name")]
    [InlineData(".Customers.Name")]
    [InlineData("dbo.Customers.")]
    public void ParseColumnPath_InvalidPath_Throws(string value)
    {
        var exception = Should.Throw<BridgeException>(() => BridgeIdentifier.ParseColumnPath(value));

        exception.Message.ShouldContain("<schema>.<table>.<column>");
    }
}
