using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.IntegrationTests.Harness;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.IntegrationTests.Tests;

[Collection(nameof(SqlServerCollection))]
public sealed class DacpacEditorIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DacpacEditorIntegrationTests(SqlServerContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
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

    [Fact]
    public async Task Edit_StripsContainmentFromPartialContainedSourceDacpac_DeploysToNonContainedTarget()
    {
        await EnsureContainedAuthEnabledAsync();

        await using var source = await SqlServerFixtureDatabase.CreateAsync(_fixture);
        await using var target = await SqlServerFixtureDatabase.CreateAsync(_fixture);

        await ExecuteMasterSqlAsync($"ALTER DATABASE [{source.DatabaseName}] SET CONTAINMENT = PARTIAL");
        await source.ExecuteSqlAsync("CREATE TABLE dbo.KeepMe (Id INT NOT NULL);");

        var dacpacPath = ExtractDacpac(source);
        try
        {
            var databaseOptionsDump = DumpDatabaseOptionsElement(dacpacPath);
            _output.WriteLine("SqlDatabaseOptions element in extracted model.xml:");
            _output.WriteLine(databaseOptionsDump);

            DacpacModelHasContainmentProperty(dacpacPath).ShouldBeTrue(
                "Pre-strip sanity check: a dacpac extracted from a CONTAINMENT=PARTIAL source database must "
                + "declare a SqlDatabaseOptions Element with a Containment Property. If this fails, the selector "
                + "in TryRemoveDatabaseContainmentProperty does not match real DacFx output and the strip silently "
                + "no-ops in production. Element dump:\n" + databaseOptionsDump);

            await DeployViaBridgeAsync(dacpacPath, target);

            (await TableCountAsync(target, "dbo", "KeepMe")).ShouldBe(1);
            // If the strip didn't actually engage, DacFx would have emitted
            // ALTER DATABASE target SET CONTAINMENT = PARTIAL. With contained-DB auth enabled on the
            // container the ALTER would succeed, leaving the target with containment = 1.
            (await ContainmentLevelAsync(target.DatabaseName)).ShouldBe(0);
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

    private async Task EnsureContainedAuthEnabledAsync()
    {
        await ExecuteMasterSqlAsync(
            "EXEC sp_configure 'show advanced options', 1; RECONFIGURE; "
            + "EXEC sp_configure 'contained database authentication', 1; RECONFIGURE;");
    }

    private async Task ExecuteMasterSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(_fixture.MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ContainmentLevelAsync(string databaseName)
    {
        await using var connection = new SqlConnection(_fixture.MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT containment FROM sys.databases WHERE name = @name;";
        command.Parameters.AddWithValue("@name", databaseName);
        command.CommandTimeout = 120;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static bool DacpacModelHasContainmentProperty(string dacpacPath)
    {
        var document = LoadDacpacModel(dacpacPath);
        return document
            .Descendants()
            .Where(e => e.Name.LocalName == "Element"
                        && string.Equals((string?)e.Attribute("Type"), "SqlDatabaseOptions", StringComparison.Ordinal))
            .SelectMany(e => e.Elements())
            .Any(p => p.Name.LocalName == "Property"
                      && string.Equals((string?)p.Attribute("Name"), "Containment", StringComparison.Ordinal));
    }

    private static string DumpDatabaseOptionsElement(string dacpacPath)
    {
        var document = LoadDacpacModel(dacpacPath);
        var elements = document
            .Descendants()
            .Where(e => e.Name.LocalName == "Element"
                        && string.Equals((string?)e.Attribute("Type"), "SqlDatabaseOptions", StringComparison.Ordinal))
            .ToList();
        if (elements.Count == 0)
        {
            return "(no Element[@Type='SqlDatabaseOptions'] found)";
        }

        return string.Join("\n\n", elements.Select(e => e.ToString()));
    }

    private static XDocument LoadDacpacModel(string dacpacPath)
    {
        using var archive = ZipFile.OpenRead(dacpacPath);
        var entry = archive.GetEntry("model.xml")
            ?? throw new InvalidOperationException($"model.xml not found in dacpac '{dacpacPath}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
