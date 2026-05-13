using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;
using Shouldly;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class ImportPlannerTests
{
    [Fact]
    public void BuildImportOrder_OrdersReferencedTablesBeforeDependents()
    {
        var parent = new TableName("dbo", "Parent");
        var child = new TableName("dbo", "Child");
        var grandChild = new TableName("dbo", "GrandChild");
        var tables = new[] { grandChild, child, parent };
        var foreignKeys = new[]
        {
            new ForeignKeyMetadata(child, parent),
            new ForeignKeyMetadata(grandChild, child)
        };

        var result = ImportPlanner.BuildImportOrder(tables, foreignKeys);

        result.ShouldBe([parent, child, grandChild]);
    }

    [Fact]
    public void BuildImportOrder_IndependentTables_UsesStableNameOrder()
    {
        var zeta = new TableName("dbo", "Zeta");
        var alpha = new TableName("dbo", "Alpha");

        var result = ImportPlanner.BuildImportOrder([zeta, alpha], []);

        result.ShouldBe([alpha, zeta]);
    }

    [Fact]
    public void BuildImportOrder_ForeignKeyCycle_Throws()
    {
        var first = new TableName("dbo", "First");
        var second = new TableName("dbo", "Second");
        var foreignKeys = new[]
        {
            new ForeignKeyMetadata(first, second),
            new ForeignKeyMetadata(second, first)
        };

        var exception = Should.Throw<BridgeException>(() => ImportPlanner.BuildImportOrder([first, second], foreignKeys));

        exception.Message.ShouldContain("foreign-key cycle");
    }
}
