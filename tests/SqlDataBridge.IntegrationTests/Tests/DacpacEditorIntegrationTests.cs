using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.SqlServer.Dac;
using Shouldly;
using Xunit;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class DacpacEditorIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public DacpacEditorIntegrationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Edit_RemoveTableFromExtractedDacpac_DeploysWithoutTable()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);

        await source.ExecuteSqlAsync("""
            CREATE TABLE dbo.KeepA (Id INT NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.KeepB (Id INT NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.ToRemove (Id INT NOT NULL);
            """);

        var dacpacPath = ExtractDacpac(source);
        try
        {
            DacpacEditor.Edit(dacpacPath, context =>
            {
                context
                    .MutateXml("model.xml", document => TryRemoveModelElement(document, "SqlTable", "[dbo].[ToRemove]"))
                    .ShouldBeTrue();
            });

            await DeployViaBridgeAsync(dacpacPath, target);

            (await TableCountAsync(target, "dbo", "KeepA")).ShouldBe(1);
            (await TableCountAsync(target, "dbo", "KeepB")).ShouldBe(1);
            (await TableCountAsync(target, "dbo", "ToRemove")).ShouldBe(0);
        }
        finally
        {
            DeleteIfExists(dacpacPath);
        }
    }

    [Fact]
    public async Task Edit_NoOpEditOnExtractedDacpac_DeploysAllTables()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);

        await source.ExecuteSqlAsync("""
            CREATE TABLE dbo.KeepA (Id INT NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.KeepB (Id INT NOT NULL PRIMARY KEY);
            """);

        var dacpacPath = ExtractDacpac(source);
        try
        {
            DacpacEditor.Edit(dacpacPath, _ => { });

            await DeployViaBridgeAsync(dacpacPath, target);

            (await TableCountAsync(target, "dbo", "KeepA")).ShouldBe(1);
            (await TableCountAsync(target, "dbo", "KeepB")).ShouldBe(1);
        }
        finally
        {
            DeleteIfExists(dacpacPath);
        }
    }

    [Fact]
    public async Task Edit_RemoveStoredProcedureFromExtractedDacpac_DeploysWithoutProcedure()
    {
        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);

        await source.ExecuteSqlAsync("CREATE TABLE dbo.KeepTable (Id INT NOT NULL PRIMARY KEY);");
        await source.ExecuteSqlAsync("CREATE PROCEDURE dbo.KeepSproc AS SELECT 1;");
        await source.ExecuteSqlAsync("CREATE PROCEDURE dbo.RemoveSproc AS SELECT 2;");

        var dacpacPath = ExtractDacpac(source);
        try
        {
            DacpacEditor.Edit(dacpacPath, context =>
            {
                context
                    .MutateXml("model.xml", document => TryRemoveModelElement(document, "SqlProcedure", "[dbo].[RemoveSproc]"))
                    .ShouldBeTrue();
            });

            var services = new DacServices(target.ConnectionString);
            using var package = DacPackage.Load(dacpacPath);
            services.Deploy(package, target.DatabaseName, upgradeExisting: true);

            (await ProcedureCountAsync(target, "dbo", "KeepSproc")).ShouldBe(1);
            (await ProcedureCountAsync(target, "dbo", "RemoveSproc")).ShouldBe(0);
            (await TableCountAsync(target, "dbo", "KeepTable")).ShouldBe(1);
        }
        finally
        {
            DeleteIfExists(dacpacPath);
        }
    }

    private static bool TryRemoveModelElement(XDocument document, string elementType, string quotedName)
    {
        var matches = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Element"
                        && string.Equals((string?)e.Attribute("Type"), elementType, StringComparison.Ordinal)
                        && string.Equals((string?)e.Attribute("Name"), quotedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (var match in matches)
        {
            match.Remove();
        }

        return true;
    }

    private static async Task DeployViaBridgeAsync(string dacpacPath, SqlServerFixtureDatabase target)
    {
        var payload = await File.ReadAllBytesAsync(dacpacPath);
        var package = new SchemaPackage(
            "dacpac",
            Path.GetFileName(dacpacPath),
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            DateTimeOffset.UtcNow,
            target.DatabaseName,
            "integration test",
            DacpacSchemaScope.Database,
            payload);

        await DacpacSchemaManager.DeployAsync(
            target.ConnectionString,
            package,
            DacpacDeploymentOptions.Default,
            allowDacpacObjectDrops: false,
            CancellationToken.None);
    }

    private static string ExtractDacpac(SqlServerFixtureDatabase source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zsb-itest-{Guid.NewGuid():N}.dacpac");
        var services = new DacServices(source.ConnectionString);
        services.Extract(
            path,
            source.DatabaseName,
            source.DatabaseName,
            new Version(1, 0, 0),
            "integration test",
            tables: null,
            extractOptions: new DacExtractOptions { ExtractAllTableData = false });
        return path;
    }

    private static Task<int> TableCountAsync(SqlServerFixtureDatabase database, string schema, string name)
    {
        return database.ScalarIntAsync(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = '{name}' AND SCHEMA_NAME(schema_id) = '{schema}';");
    }

    private static Task<int> ProcedureCountAsync(SqlServerFixtureDatabase database, string schema, string name)
    {
        return database.ScalarIntAsync(
            $"SELECT COUNT(*) FROM sys.procedures WHERE name = '{name}' AND SCHEMA_NAME(schema_id) = '{schema}';");
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
