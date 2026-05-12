using System.Reflection;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Harness;

internal static class SqlScriptLoader
{
    public static string LoadEmbeddedScript(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(x => x.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
