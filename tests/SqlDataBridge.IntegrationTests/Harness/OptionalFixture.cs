namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

/// <summary>
/// Locates large, gitignored, opt-in fixture files (real-world dacpac packages, WideWorldImporters backups)
/// that are dropped in by a developer rather than committed. Resolution order:
/// <list type="number">
///   <item>An explicit absolute path from <paramref name="environmentVariable"/> (for CI / nightly jobs).</item>
///   <item><c>Fixtures/&lt;fileName&gt;</c> next to the test assembly.</item>
///   <item><c>tests/SqlDataBridge.IntegrationTests/Fixtures/&lt;fileName&gt;</c> found by walking up from the assembly
///   directory (useful when the runner copies binaries but not unmanaged data files).</item>
/// </list>
/// Returns <see langword="false"/> when the fixture is absent so the caller can skip gracefully and keep CI green.
/// </summary>
internal static class OptionalFixture
{
    public static bool TryLocate(string fileName, string? environmentVariable, out string path)
    {
        if (!string.IsNullOrWhiteSpace(environmentVariable))
        {
            var configured = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                path = configured;
                return true;
            }
        }

        var assemblyDirectory = Path.GetDirectoryName(typeof(OptionalFixture).Assembly.Location);
        if (assemblyDirectory is not null)
        {
            var candidate = Path.Combine(assemblyDirectory, "Fixtures", fileName);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        var dir = assemblyDirectory is null ? null : new DirectoryInfo(assemblyDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "tests",
                "SqlDataBridge.IntegrationTests",
                "Fixtures",
                fileName);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            dir = dir.Parent;
        }

        path = string.Empty;
        return false;
    }
}
