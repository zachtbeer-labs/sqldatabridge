using Shouldly;
using Zachtbeer.SqlDataBridge.Internal;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class BatchPlannerTests
{
    [Fact]
    public void GetEffectiveBatchSize_SmallTable_KeepsConfiguredBatchSize()
    {
        var options = new ExportOptions { BatchSize = 1_000 };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 100, estimatedBytes: 100_000);

        batchSize.ShouldBe(1_000);
    }

    [Fact]
    public void GetEffectiveBatchSize_LargeTableByBytes_UsesLargeTableBatchSize()
    {
        var options = new ExportOptions
        {
            BatchSize = 1_000,
            LargeTableThresholdBytes = 50_000_000,
            LargeTableBatchSize = 250
        };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 10_000, estimatedBytes: 50_000_000);

        batchSize.ShouldBe(250);
    }

    [Fact]
    public void GetEffectiveBatchSize_LargeTableByRows_UsesLargeTableBatchSizeWhenBytesAreUnknown()
    {
        var options = new ImportOptions
        {
            BatchSize = 1_000,
            LargeTableRowThreshold = 100_000,
            LargeTableBatchSize = 250
        };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 100_000, estimatedBytes: 0);

        batchSize.ShouldBe(250);
    }

    [Fact]
    public void GetEffectiveBatchSize_WideRows_ReducesByMaxBatchBytes()
    {
        var options = new ExportOptions
        {
            BatchSize = 1_000,
            LargeTableThresholdBytes = 1_000_000_000,
            MaxBatchBytes = 1_000_000
        };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 100, estimatedBytes: 10_000_000);

        batchSize.ShouldBe(10);
    }

    [Fact]
    public void GetEffectiveBatchSize_NeverIncreasesConfiguredBatchSize()
    {
        var options = new ExportOptions
        {
            BatchSize = 50,
            LargeTableThresholdBytes = 1,
            LargeTableBatchSize = 250
        };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 1_000, estimatedBytes: 1_000);

        batchSize.ShouldBe(50);
    }

    [Fact]
    public void GetEffectiveBatchSize_DisabledAdaptiveBatching_KeepsConfiguredBatchSize()
    {
        var options = new ExportOptions
        {
            BatchSize = 1_000,
            AdaptiveBatchingEnabled = false,
            LargeTableThresholdBytes = 1,
            LargeTableBatchSize = 1
        };

        var batchSize = BatchPlanner.GetEffectiveBatchSize(options, estimatedRows: 1_000_000, estimatedBytes: 1_000_000_000);

        batchSize.ShouldBe(1_000);
    }
}
