using Xunit.Abstractions;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

/// <summary>
/// Restores a WideWorldImporters backup into the shared container at most once per process and hands the read-only
/// source database to every test that asks for it. Restoring WWI is expensive (~minutes), and the exporter never
/// mutates the source, so the same restored database is safely reused across the round-trip and gap tests for a
/// variant. Returns <see langword="null"/> when the backup is not present so callers skip and the suite stays green.
/// </summary>
internal static class SharedWideWorldImportersSource
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, Task<SqlServerFixtureDatabase>> Restores = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<SqlServerFixtureDatabase?> TryGetAsync(
        SqlServerContainerFixture fixture,
        string bakFileName,
        string bakEnvironmentVariable,
        ITestOutputHelper output)
    {
        if (!OptionalFixture.TryLocate(bakFileName, bakEnvironmentVariable, out var bakPath))
        {
            output.WriteLine(
                $"[skip] WideWorldImporters backup '{bakFileName}' not found next to the test assembly and "
                + $"{bakEnvironmentVariable} is unset; skipping. Drop the .bak in Fixtures/ or set the env var to run.");
            return null;
        }

        await Gate.WaitAsync();
        try
        {
            if (!Restores.TryGetValue(bakPath, out var restore))
            {
                output.WriteLine($"Restoring WideWorldImporters from: {bakPath} (first use; subsequent tests reuse it)");
                restore = SqlServerFixtureDatabase.RestoreFromBakAsync(fixture, bakPath);
                Restores[bakPath] = restore;
            }

            return await restore;
        }
        finally
        {
            Gate.Release();
        }
    }
}
