using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.Internal;

internal static class DacpacSchemaManager
{
    public static async Task<SchemaPackage> ExtractAsync(string connectionString, ExportPlan plan, DacpacCaptureOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);

        var databaseName = GetDatabaseName(connectionString, "capture schema");
        var path = CreateTemporaryDacpacPath();
        var reducedPath = options.SchemaScope == DacpacSchemaScope.SelectedExportTables
            ? CreateTemporaryDacpacPath()
            : null;

        try
        {
            var services = new DacServices(connectionString);
            var applicationVersion = typeof(BridgeVersion).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var extractOptions = CreateExtractOptions(options);

            await Task.Run(
                () => services.Extract(
                    path,
                    databaseName,
                    databaseName,
                    applicationVersion,
                    "Extracted by Zachtbeer.SqlDataBridge.",
                    tables: null,
                    extractOptions: extractOptions,
                    cancellationToken: cancellationToken),
                cancellationToken);

            var packagePath = options.SchemaScope switch
            {
                DacpacSchemaScope.Database => path,
                DacpacSchemaScope.SelectedExportTables => await BuildSelectedTablesPackageAsync(
                    path,
                    reducedPath ?? throw new InvalidOperationException("Reduced dacpac path was not created."),
                    databaseName,
                    applicationVersion,
                    plan,
                    cancellationToken),
                _ => throw new BridgeException($"Dacpac schema scope '{options.SchemaScope}' is not supported.")
            };

            var payload = await File.ReadAllBytesAsync(packagePath, cancellationToken);
            return new SchemaPackage(
                "dacpac",
                $"{databaseName}.dacpac",
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                DateTimeOffset.UtcNow,
                databaseName,
                typeof(DacServices).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(DacServices).Assembly.GetName().Version?.ToString(),
                options.SchemaScope,
                payload);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not BridgeException)
        {
            throw new BridgeException($"Failed to extract dacpac schema from source database '{databaseName}'.", exception);
        }
        finally
        {
            DeleteTemporaryFile(path);
            if (reducedPath is not null)
            {
                DeleteTemporaryFile(reducedPath);
            }
        }
    }

    public static async Task DeployAsync(
        string connectionString,
        SchemaPackage package,
        DacpacDeploymentOptions options,
        bool allowDacpacObjectDrops,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(package.PackageType, "dacpac", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeException($"Schema package type '{package.PackageType}' is not supported for dacpac deployment.");
        }

        var targetDatabaseName = GetDatabaseName(connectionString, "deploy dacpac schema");
        var actualHash = Convert.ToHexString(SHA256.HashData(package.Payload)).ToLowerInvariant();
        if (!string.Equals(actualHash, package.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeException("SQLite package is invalid: stored dacpac payload hash does not match metadata.");
        }

        if (package.SchemaScope == DacpacSchemaScope.SelectedExportTables && options.AllowObjectDrops)
        {
            throw new BridgeException("DacpacDeploymentOptions.AllowObjectDrops cannot be used with a selected-table dacpac schema package because unrelated target objects would be compared against a reduced source model.");
        }

        var path = CreateTemporaryDacpacPath();
        try
        {
            await File.WriteAllBytesAsync(path, package.Payload, cancellationToken);

            var services = new DacServices(connectionString);
            using var dacpac = DacPackage.Load(path);
            var deployOptions = CreateDeployOptions(options, allowDacpacObjectDrops);

            await Task.Run(
                () => services.Deploy(
                    dacpac,
                    targetDatabaseName,
                    upgradeExisting: true,
                    options: deployOptions,
                    cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not BridgeException)
        {
            throw new BridgeException($"Failed to deploy dacpac schema to target database '{targetDatabaseName}'.", exception);
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    private static Task<string> BuildSelectedTablesPackageAsync(
        string sourcePath,
        string destinationPath,
        string databaseName,
        Version applicationVersion,
        ExportPlan plan,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var sourceModel = new TSqlModel(sourcePath, DacSchemaModelStorageType.Memory, cancellationToken);
                using var selectedModel = new TSqlModel(sourceModel.Version, sourceModel.CopyModelOptions());
                var selectedTableNames = plan.Tables
                    .Select(t => t.Name.FullName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var scripts = new List<(string Name, string Script)>();

                foreach (var schema in plan.Tables.Select(t => t.Name.Schema).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var schemaObject = sourceModel.GetObject(
                        ModelSchema.Schema,
                        new ObjectIdentifier([schema]),
                        DacQueryScopes.UserDefined);
                    if (TryGetScript(schemaObject, out var schemaScript))
                    {
                        scripts.Add(($"schema:{schema}", schemaScript));
                    }
                }

                foreach (var table in plan.Tables.OrderBy(t => t.Name.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tableObject = sourceModel.GetObject(
                        ModelSchema.Table,
                        new ObjectIdentifier([table.Name.Schema, table.Name.Name]),
                        DacQueryScopes.UserDefined);
                    if (tableObject is null)
                    {
                        throw new BridgeException($"Failed to build selected-table dacpac because source model table '{table.Name.FullName}' was not found.");
                    }

                    AddScriptableDependencies(tableObject, selectedTableNames, scripts);
                    AddScriptableChildDependencies(tableObject, selectedTableNames, scripts);
                    if (!tableObject.TryGetScript(out var tableScript))
                    {
                        throw new BridgeException($"Failed to build selected-table dacpac because source model table '{table.Name.FullName}' could not be scripted.");
                    }

                    scripts.Add(($"table:{table.Name.FullName}", tableScript));
                    AddScriptableChildren(tableObject, selectedTableNames, scripts);
                }

                foreach (var script in scripts
                             .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First()))
                {
                    selectedModel.AddObjects(script.Script);
                }

                var metadata = new PackageMetadata
                {
                    Name = databaseName,
                    Version = applicationVersion.ToString(),
                    Description = "Selected export-table schema extracted by Zachtbeer.SqlDataBridge."
                };
                DacPackageExtensions.BuildPackage(destinationPath, selectedModel, metadata);
                return destinationPath;
            },
            cancellationToken);
    }

    private static void AddScriptableDependencies(
        TSqlObject tableObject,
        ISet<string> selectedTableNames,
        List<(string Name, string Script)> scripts)
    {
        foreach (var dependency in tableObject.GetReferenced(DacQueryScopes.UserDefined))
        {
            if (dependency.ObjectType == ModelSchema.Table)
            {
                var tableName = ToFullName(dependency.Name);
                if (selectedTableNames.Contains(tableName))
                {
                    continue;
                }
            }

            if (dependency.ObjectType == ModelSchema.Schema)
            {
                continue;
            }

            if (TryGetScript(dependency, out var script))
            {
                scripts.Add(($"{dependency.ObjectType}:{dependency.Name}", script));
            }
        }
    }

    private static void AddScriptableChildDependencies(
        TSqlObject tableObject,
        ISet<string> selectedTableNames,
        List<(string Name, string Script)> scripts)
    {
        foreach (var child in tableObject.GetChildren(DacQueryScopes.UserDefined))
        {
            if (child.ObjectType == ModelSchema.ForeignKeyConstraint
                && ReferencesUnselectedTable(child, selectedTableNames))
            {
                continue;
            }

            AddScriptableDependencies(child, selectedTableNames, scripts);
        }
    }

    private static void AddScriptableChildren(
        TSqlObject tableObject,
        ISet<string> selectedTableNames,
        List<(string Name, string Script)> scripts)
    {
        foreach (var child in tableObject.GetChildren(DacQueryScopes.UserDefined))
        {
            if (child.ObjectType == ModelSchema.ForeignKeyConstraint
                && ReferencesUnselectedTable(child, selectedTableNames))
            {
                continue;
            }

            if (TryGetScript(child, out var script))
            {
                scripts.Add(($"{child.ObjectType}:{child.Name}", script));
            }
        }
    }

    private static bool ReferencesUnselectedTable(TSqlObject sqlObject, ISet<string> selectedTableNames)
    {
        return sqlObject
            .GetReferenced(DacQueryScopes.UserDefined)
            .Where(referenced => referenced.ObjectType == ModelSchema.Table)
            .Select(referenced => ToFullName(referenced.Name))
            .Any(tableName => !selectedTableNames.Contains(tableName));
    }

    private static bool TryGetScript(TSqlObject? sqlObject, out string script)
    {
        script = string.Empty;
        return sqlObject is not null
            && sqlObject.TryGetScript(out script)
            && !string.IsNullOrWhiteSpace(script);
    }

    private static string ToFullName(ObjectIdentifier identifier)
    {
        return string.Join(".", identifier.Parts.Select(p => p.Trim('[', ']')));
    }

    internal static DacExtractOptions CreateExtractOptions(DacpacCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DacExtractOptions
        {
            ExtractAllTableData = false,
            ExtractReferencedServerScopedElements = options.ExtractReferencedServerScopedElements,
            ExtractApplicationScopedObjectsOnly = options.ExtractApplicationScopedObjectsOnly,
            IgnorePermissions = options.IgnorePermissions,
            IgnoreUserLoginMappings = options.IgnoreUserLoginMappings,
            VerifyExtraction = options.VerifyExtraction
        };
    }

    internal static DacDeployOptions CreateDeployOptions(DacpacDeploymentOptions options, bool allowDacpacObjectDrops = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        var excludedObjectTypes = new List<ObjectType>();
        if (!options.DeployUsers)
        {
            excludedObjectTypes.Add(ObjectType.Users);
        }

        if (!options.DeployLogins)
        {
            excludedObjectTypes.Add(ObjectType.Logins);
            excludedObjectTypes.Add(ObjectType.LinkedServerLogins);
        }

        if (!options.DeployPermissions)
        {
            excludedObjectTypes.Add(ObjectType.Permissions);
        }

        if (!options.DeployRoleMembership)
        {
            excludedObjectTypes.Add(ObjectType.RoleMembership);
            excludedObjectTypes.Add(ObjectType.ServerRoleMembership);
        }

        if (!options.DeployDatabaseFiles)
        {
            excludedObjectTypes.Add(ObjectType.Files);
            excludedObjectTypes.Add(ObjectType.Filegroups);
        }

        return new DacDeployOptions
        {
            AllowIncompatiblePlatform = options.AllowIncompatiblePlatform,
            BlockOnPossibleDataLoss = options.BlockOnPossibleDataLoss,
            DropObjectsNotInSource = options.AllowObjectDrops || allowDacpacObjectDrops,
            ExcludeObjectTypes = excludedObjectTypes.ToArray(),
            IgnoreFileAndLogFilePath = !options.DeployDatabaseFiles,
            IgnoreFilegroupPlacement = !options.DeployDatabaseFiles,
            IgnoreFileSize = !options.DeployDatabaseFiles,
            IncludeTransactionalScripts = true,
            ScriptDatabaseOptions = options.DeployDatabaseOptions,
            VerifyDeployment = options.VerifyDeployment
        };
    }

    private static string GetDatabaseName(string connectionString, string operation)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            throw new BridgeException($"Cannot {operation} because the SQL Server connection string does not specify a database.");
        }

        return builder.InitialCatalog;
    }

    private static string CreateTemporaryDacpacPath()
    {
        return Path.Combine(Path.GetTempPath(), $"zsb-schema-{Guid.NewGuid():N}.dacpac");
    }

    private static void DeleteTemporaryFile(string path)
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
            // Best effort cleanup only; preserve the operation failure.
        }
    }
}
