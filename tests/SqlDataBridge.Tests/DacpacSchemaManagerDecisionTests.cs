using Shouldly;
using Xunit;
using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class DacpacSchemaManagerDecisionTests
{
    // EngineEdition cheat-sheet for the cases below:
    //   2  = Standard / on-prem  | 3  = Enterprise / on-prem | 4  = Express / on-prem
    //   5  = Azure SQL Database  | 8  = Azure SQL MI         | 11 = Azure SQL Edge
    //   12 = Azure Synapse SQL pool
    [Theory]
    // Azure source -> on-prem target: needs the rewrite.
    [InlineData(5, 3, true)]
    [InlineData(8, 3, true)]
    [InlineData(11, 2, true)]
    [InlineData(12, 4, true)]
    // Azure source -> Azure target: no rewrite needed.
    [InlineData(5, 5, false)]
    [InlineData(8, 5, false)]
    // On-prem source -> on-prem target: no rewrite needed.
    [InlineData(3, 3, false)]
    [InlineData(2, 4, false)]
    // On-prem source -> Azure target: still no rewrite (the rewrite is Azure->on-prem-specific).
    [InlineData(3, 5, false)]
    public void ShouldAdaptAzureSourceForOnPremTarget_WithKnownSourceEdition_ReturnsExpected(
        int sourceEdition, int targetEdition, bool expected)
    {
        DacpacSchemaManager
            .ShouldAdaptAzureSourceForOnPremTarget(sourceEdition, targetEdition)
            .ShouldBe(expected);
    }

    [Theory]
    // Unknown source falls back to "target is non-Azure => rewrite, just in case". This preserves the
    // pre-format-v4 behaviour for packages and tests that construct a SchemaPackage without a source stamp.
    [InlineData(3, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    // Unknown source against an Azure target: never rewrite — Azure tolerates Azure-extracted models.
    [InlineData(5, false)]
    [InlineData(8, false)]
    public void ShouldAdaptAzureSourceForOnPremTarget_WithUnknownSourceEdition_FallsBackToTargetCheck(
        int targetEdition, bool expected)
    {
        DacpacSchemaManager
            .ShouldAdaptAzureSourceForOnPremTarget(sourceEngineEdition: null, targetEdition)
            .ShouldBe(expected);
    }
}
