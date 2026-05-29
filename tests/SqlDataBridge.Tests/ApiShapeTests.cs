using System.Reflection;
using Microsoft.SqlServer.Dac;
using Shouldly;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;
using Xunit;

namespace Zachtbeer.SqlDataBridge.Tests;

public sealed class ApiShapeTests
{
    [Fact]
    public void EntryPoints_RemainInBaseNamespace()
    {
        typeof(SqlDataBridgeExporter).Namespace.ShouldBe("Zachtbeer.SqlDataBridge");
        typeof(SqlDataBridgeImporter).Namespace.ShouldBe("Zachtbeer.SqlDataBridge");
        typeof(global::Zachtbeer.SqlDataBridge.SqlDataBridge).Namespace.ShouldBe("Zachtbeer.SqlDataBridge");
    }

    [Fact]
    public void SupportingPublicTypes_AreInModelsNamespace()
    {
        var types = new[]
        {
            typeof(ExportOptions),
            typeof(ImportOptions),
            typeof(BridgeOptions),
            typeof(BridgeResult),
            typeof(BridgePreflightResult),
            typeof(BridgePackageManifest),
            typeof(BridgeTableManifest),
            typeof(BridgeColumnManifest),
            typeof(BridgeProgress),
            typeof(BridgeProgressKind),
            typeof(BridgeException),
            typeof(DataPackageReader),
            typeof(SchemaCaptureMode),
            typeof(SchemaDeploymentMode),
            typeof(DacpacCaptureOptions),
            typeof(DacpacDeploymentOptions),
            typeof(DacpacSchemaScope),
            typeof(ExportTableSelectionMode),
            typeof(GlobalWhereClause),
            typeof(PerTableWhereClause)
        };

        types.Select(type => type.Namespace).Distinct().ShouldBe(["Zachtbeer.SqlDataBridge.Models"]);
    }

    [Fact]
    public void StaticFacade_ExposesSimpleExportAndImportMethods()
    {
        var export = FindMethod(
            typeof(global::Zachtbeer.SqlDataBridge.SqlDataBridge),
            nameof(global::Zachtbeer.SqlDataBridge.SqlDataBridge.ExportAsync),
            typeof(string),
            typeof(string),
            typeof(ExportOptions),
            typeof(CancellationToken));
        var import = FindMethod(
            typeof(global::Zachtbeer.SqlDataBridge.SqlDataBridge),
            nameof(global::Zachtbeer.SqlDataBridge.SqlDataBridge.ImportAsync),
            typeof(string),
            typeof(string),
            typeof(ImportOptions),
            typeof(CancellationToken));

        export.ShouldNotBeNull();
        import.ShouldNotBeNull();
    }

    [Fact]
    public void Exporter_ExposesPreferredExportOptionsOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeExporter),
            nameof(SqlDataBridgeExporter.ExportAsync),
            typeof(string),
            typeof(string),
            typeof(ExportOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void Exporter_KeepsSharedOptionsCompatibilityOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeExporter),
            nameof(SqlDataBridgeExporter.ExportAsync),
            typeof(string),
            typeof(string),
            typeof(BridgeOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void Exporter_ExposesPreflightOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeExporter),
            nameof(SqlDataBridgeExporter.PreflightAsync),
            typeof(string),
            typeof(ExportOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void Importer_ExposesPreferredImportOptionsOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeImporter),
            nameof(SqlDataBridgeImporter.ImportAsync),
            typeof(string),
            typeof(string),
            typeof(ImportOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void Importer_KeepsSharedOptionsCompatibilityOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeImporter),
            nameof(SqlDataBridgeImporter.ImportAsync),
            typeof(string),
            typeof(string),
            typeof(BridgeOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void Importer_ExposesPreflightOverload()
    {
        var method = FindMethod(
            typeof(SqlDataBridgeImporter),
            nameof(SqlDataBridgeImporter.PreflightAsync),
            typeof(string),
            typeof(string),
            typeof(ImportOptions),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void PackageReader_ExposesManifestReader()
    {
        var method = FindMethod(
            typeof(DataPackageReader),
            nameof(DataPackageReader.ReadManifestAsync),
            typeof(string),
            typeof(CancellationToken));

        method.ShouldNotBeNull();
    }

    [Fact]
    public void ExportOptions_CarryTableSelectionAndBatchSettings()
    {
        var options = new ExportOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.*"],
            ExcludeColumns = ["dbo.Customers.LegacyCode"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.Customers", "Active = 1")],
            DataTablePrefix = "custom_data",
            BatchSize = 250,
            AdaptiveBatchingEnabled = true,
            LargeTableThresholdBytes = 10_000_000,
            LargeTableRowThreshold = 50_000,
            LargeTableBatchSize = 100,
            MaxBatchBytes = 1_000_000,
            CommandTimeout = 120,
            Progress = new Progress<BridgeProgress>(),
            OverwriteExistingPackage = true,
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            DacpacCaptureOptions = new DacpacCaptureOptions
            {
                SchemaScope = DacpacSchemaScope.Database,
                ExtractReferencedServerScopedElements = true,
                IgnorePermissions = false,
                IgnoreUserLoginMappings = false
            }
        };

        options.TableSelection.ShouldBe(ExportTableSelectionMode.Only);
        options.Tables.ShouldBe(["dbo.*"]);
        options.ExcludeColumns.ShouldBe(["dbo.Customers.LegacyCode"]);
        options.GlobalWhereClauses.ShouldBe([new GlobalWhereClause("TenantId", "TenantId = 123")]);
        options.PerTableWhereClauses.ShouldBe([new PerTableWhereClause("dbo.Customers", "Active = 1")]);
        options.DataTablePrefix.ShouldBe("custom_data");
        options.BatchSize.ShouldBe(250);
        options.AdaptiveBatchingEnabled.ShouldBeTrue();
        options.LargeTableThresholdBytes.ShouldBe(10_000_000);
        options.LargeTableRowThreshold.ShouldBe(50_000);
        options.LargeTableBatchSize.ShouldBe(100);
        options.MaxBatchBytes.ShouldBe(1_000_000);
        options.CommandTimeout.ShouldBe(120);
        options.Progress.ShouldNotBeNull();
        options.OverwriteExistingPackage.ShouldBeTrue();
        options.SchemaCaptureMode.ShouldBe(SchemaCaptureMode.Dacpac);
        options.DacpacCaptureOptions.SchemaScope.ShouldBe(DacpacSchemaScope.Database);
        options.DacpacCaptureOptions.ExtractReferencedServerScopedElements.ShouldBeTrue();
        options.DacpacCaptureOptions.IgnorePermissions.ShouldBeFalse();
        options.DacpacCaptureOptions.IgnoreUserLoginMappings.ShouldBeFalse();
    }

    [Fact]
    public void ExportOptions_ExposeExplicitTableSelectionOnly()
    {
        typeof(ExportOptions).GetProperty("TableSelection").ShouldNotBeNull();
        typeof(ExportOptions).GetProperty("Tables").ShouldNotBeNull();
        typeof(ExportOptions).GetProperty("DataTablePrefix").ShouldNotBeNull();
        typeof(ExportOptions).GetProperty("IncludeTables").ShouldBeNull();
        typeof(ExportOptions).GetProperty("ExcludeTables").ShouldBeNull();
        typeof(BridgeOptions).GetProperty("TableSelection").ShouldNotBeNull();
        typeof(BridgeOptions).GetProperty("Tables").ShouldNotBeNull();
        typeof(BridgeOptions).GetProperty("DataTablePrefix").ShouldNotBeNull();
        typeof(BridgeOptions).GetProperty("IncludeTables").ShouldBeNull();
        typeof(BridgeOptions).GetProperty("ExcludeTables").ShouldBeNull();
    }

    [Fact]
    public void ImportOptions_CarryImportOnlyBatchSettings()
    {
        var options = new ImportOptions
        {
            BatchSize = 500,
            AdaptiveBatchingEnabled = true,
            LargeTableThresholdBytes = 20_000_000,
            LargeTableRowThreshold = 75_000,
            LargeTableBatchSize = 125,
            MaxBatchBytes = 2_000_000,
            ValidationCommandTimeout = 90,
            BulkCopyTimeout = 180,
            Progress = new Progress<BridgeProgress>(),
            SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
            DacpacDeploymentOptions = new DacpacDeploymentOptions
            {
                AllowIncompatiblePlatform = true,
                BlockOnPossibleDataLoss = false,
                AllowObjectDrops = true,
                DeployUsers = true
            }
        };

        options.BatchSize.ShouldBe(500);
        options.AdaptiveBatchingEnabled.ShouldBeTrue();
        options.LargeTableThresholdBytes.ShouldBe(20_000_000);
        options.LargeTableRowThreshold.ShouldBe(75_000);
        options.LargeTableBatchSize.ShouldBe(125);
        options.MaxBatchBytes.ShouldBe(2_000_000);
        options.ValidationCommandTimeout.ShouldBe(90);
        options.BulkCopyTimeout.ShouldBe(180);
        options.Progress.ShouldNotBeNull();
        options.SchemaDeploymentMode.ShouldBe(SchemaDeploymentMode.DeployDacpac);
        options.DacpacDeploymentOptions.AllowIncompatiblePlatform.ShouldBeTrue();
        options.DacpacDeploymentOptions.BlockOnPossibleDataLoss.ShouldBeFalse();
        options.DacpacDeploymentOptions.AllowObjectDrops.ShouldBeTrue();
        options.DacpacDeploymentOptions.DeployUsers.ShouldBeTrue();
        typeof(ImportOptions).GetProperties().ShouldNotContain(p => p.Name.Contains("Tables", StringComparison.Ordinal));
        typeof(ImportOptions).GetProperties().ShouldNotContain(p => p.Name.Contains("Columns", StringComparison.Ordinal));
    }

    [Fact]
    public void DacpacOptions_DefaultToConservativeSchemaBehavior()
    {
        var capture = new DacpacCaptureOptions();
        var deployment = new DacpacDeploymentOptions();

        ((int)DacpacSchemaScope.Database).ShouldBe(0);
        ((int)DacpacSchemaScope.SelectedExportTables).ShouldBe(1);
        capture.SchemaScope.ShouldBe(DacpacSchemaScope.Database);
        capture.ExtractReferencedServerScopedElements.ShouldBeFalse();
        capture.ExtractApplicationScopedObjectsOnly.ShouldBeFalse();
        capture.IgnorePermissions.ShouldBeTrue();
        capture.IgnoreUserLoginMappings.ShouldBeTrue();
        capture.VerifyExtraction.ShouldBeTrue();

        deployment.AllowIncompatiblePlatform.ShouldBeFalse();
        deployment.BlockOnPossibleDataLoss.ShouldBeTrue();
        deployment.AllowObjectDrops.ShouldBeFalse();
        deployment.DeployUsers.ShouldBeFalse();
        deployment.DeployLogins.ShouldBeFalse();
        deployment.DeployPermissions.ShouldBeFalse();
        deployment.DeployRoleMembership.ShouldBeFalse();
        deployment.DeployDatabaseFiles.ShouldBeFalse();
        deployment.DeployDatabaseOptions.ShouldBeFalse();
        deployment.VerifyDeployment.ShouldBeTrue();
    }

    [Fact]
    public void DacpacDeploymentOptions_MapToDacFxConservativeDefaults()
    {
        var options = DacpacSchemaManager.CreateDeployOptions(new DacpacDeploymentOptions());

        options.AllowIncompatiblePlatform.ShouldBeFalse();
        options.BlockOnPossibleDataLoss.ShouldBeTrue();
        options.DropObjectsNotInSource.ShouldBeFalse();
        options.IncludeTransactionalScripts.ShouldBeTrue();
        options.VerifyDeployment.ShouldBeTrue();
        options.ExcludeObjectTypes.ShouldContain(ObjectType.Users);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.Logins);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.LinkedServerLogins);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.Permissions);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.RoleMembership);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.ServerRoleMembership);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.Files);
        options.ExcludeObjectTypes.ShouldContain(ObjectType.Filegroups);
        options.IgnoreFileAndLogFilePath.ShouldBeTrue();
        options.IgnoreFilegroupPlacement.ShouldBeTrue();
        options.IgnoreFileSize.ShouldBeTrue();
        options.ScriptDatabaseOptions.ShouldBeFalse();
    }

    [Fact]
    public void DacpacDeploymentOptions_MapOptInsToDacFx()
    {
        var options = DacpacSchemaManager.CreateDeployOptions(
            new DacpacDeploymentOptions
            {
                AllowIncompatiblePlatform = true,
                BlockOnPossibleDataLoss = false,
                AllowObjectDrops = true,
                DeployUsers = true,
                DeployLogins = true,
                DeployPermissions = true,
                DeployRoleMembership = true,
                DeployDatabaseFiles = true,
                DeployDatabaseOptions = true,
                VerifyDeployment = false
            });

        options.AllowIncompatiblePlatform.ShouldBeTrue();
        options.BlockOnPossibleDataLoss.ShouldBeFalse();
        options.DropObjectsNotInSource.ShouldBeTrue();
        options.VerifyDeployment.ShouldBeFalse();
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.Users);
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.Logins);
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.Permissions);
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.RoleMembership);
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.Files);
        (options.ExcludeObjectTypes ?? []).ShouldNotContain(ObjectType.Filegroups);
        options.IgnoreFileAndLogFilePath.ShouldBeFalse();
        options.IgnoreFilegroupPlacement.ShouldBeFalse();
        options.IgnoreFileSize.ShouldBeFalse();
        options.ScriptDatabaseOptions.ShouldBeTrue();
    }

    [Fact]
    public void DacpacCaptureOptions_MapToDacFx()
    {
        var options = DacpacSchemaManager.CreateExtractOptions(new DacpacCaptureOptions
        {
            ExtractReferencedServerScopedElements = true,
            ExtractApplicationScopedObjectsOnly = true,
            IgnorePermissions = false,
            IgnoreUserLoginMappings = false,
            VerifyExtraction = false
        });

        options.ExtractAllTableData.ShouldBeFalse();
        options.ExtractReferencedServerScopedElements.ShouldBeTrue();
        options.ExtractApplicationScopedObjectsOnly.ShouldBeTrue();
        options.IgnorePermissions.ShouldBeFalse();
        options.IgnoreUserLoginMappings.ShouldBeFalse();
        options.VerifyExtraction.ShouldBeFalse();
    }

    [Fact]
    public void PlannedManifest_ReportsConfiguredDacpacScope()
    {
        var table = new TableName("dbo", "Customers");
        var plan = new ExportPlan(
            [
                new TableMetadata(
                    table,
                    BridgeIdentifier.ToSqliteDataTableName(table),
                    [
                        new ColumnMetadata(table, "Id", 1, "int", 4, 10, 0, false, true, false, null, false)
                    ])
            ],
            [],
            [table],
            [],
            [],
            [],
            "hash");

        var manifest = SqlitePackage.CreatePlannedManifest(
            plan,
            new ExportOptions
            {
                SchemaCaptureMode = SchemaCaptureMode.Dacpac,
                DacpacCaptureOptions = new DacpacCaptureOptions { SchemaScope = DacpacSchemaScope.SelectedExportTables }
            });

        manifest.ContainsDacpac.ShouldBeTrue();
        manifest.DacpacSchemaScope.ShouldBe(DacpacSchemaScope.SelectedExportTables);
    }

    [Fact]
    public async Task DeployAsync_SelectedTableDacpacRejectsObjectDrops()
    {
        var payload = Array.Empty<byte>();
        var package = new SchemaPackage(
            "dacpac",
            "test.dacpac",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
            DateTimeOffset.UtcNow,
            "Target",
            "test",
            DacpacSchemaScope.SelectedExportTables,
            payload);

        var exception = await Should.ThrowAsync<BridgeException>(() =>
            DacpacSchemaManager.DeployAsync(
                "Server=localhost;Database=Target;Integrated Security=true;Trust Server Certificate=true;",
                package,
                new DacpacDeploymentOptions { AllowObjectDrops = true },
                allowDacpacObjectDrops: false,
                CancellationToken.None));

        exception.Message.ShouldContain("AllowObjectDrops cannot be used");
    }

    [Fact]
    public void SharedOptions_MapToExportAndImportOptions()
    {
        var shared = new BridgeOptions
        {
            TableSelection = ExportTableSelectionMode.Only,
            Tables = ["dbo.Customers"],
            ExcludeColumns = ["dbo.Customers.LegacyCode"],
            GlobalWhereClauses = [new GlobalWhereClause("TenantId", "TenantId = 123")],
            PerTableWhereClauses = [new PerTableWhereClause("dbo.Customers", "Active = 1")],
            DataTablePrefix = "",
            BatchSize = 750,
            AdaptiveBatchingEnabled = false,
            LargeTableThresholdBytes = 30_000_000,
            LargeTableRowThreshold = 80_000,
            LargeTableBatchSize = 150,
            MaxBatchBytes = 3_000_000,
            ExportCommandTimeout = 60,
            ImportValidationCommandTimeout = 70,
            ImportBulkCopyTimeout = 80,
            Progress = new Progress<BridgeProgress>(),
            OverwriteExistingPackage = true,
            SchemaCaptureMode = SchemaCaptureMode.Dacpac,
            DacpacCaptureOptions = new DacpacCaptureOptions { IgnorePermissions = false },
            SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
            DacpacDeploymentOptions = new DacpacDeploymentOptions
            {
                AllowIncompatiblePlatform = true,
                AllowObjectDrops = true
            }
        };

        var export = shared.ToExportOptions();
        var import = shared.ToImportOptions();

        export.TableSelection.ShouldBe(shared.TableSelection);
        export.Tables.ShouldBe(shared.Tables);
        export.ExcludeColumns.ShouldBe(shared.ExcludeColumns);
        export.GlobalWhereClauses.ShouldBe(shared.GlobalWhereClauses);
        export.PerTableWhereClauses.ShouldBe(shared.PerTableWhereClauses);
        export.DataTablePrefix.ShouldBe(shared.DataTablePrefix);
        export.BatchSize.ShouldBe(shared.BatchSize);
        export.AdaptiveBatchingEnabled.ShouldBe(shared.AdaptiveBatchingEnabled);
        export.LargeTableThresholdBytes.ShouldBe(shared.LargeTableThresholdBytes);
        export.LargeTableRowThreshold.ShouldBe(shared.LargeTableRowThreshold);
        export.LargeTableBatchSize.ShouldBe(shared.LargeTableBatchSize);
        export.MaxBatchBytes.ShouldBe(shared.MaxBatchBytes);
        export.CommandTimeout.ShouldBe(shared.ExportCommandTimeout);
        export.Progress.ShouldBeSameAs(shared.Progress);
        export.OverwriteExistingPackage.ShouldBe(shared.OverwriteExistingPackage);
        export.SchemaCaptureMode.ShouldBe(shared.SchemaCaptureMode);
        export.DacpacCaptureOptions.ShouldBeSameAs(shared.DacpacCaptureOptions);
        import.BatchSize.ShouldBe(shared.BatchSize);
        import.AdaptiveBatchingEnabled.ShouldBe(shared.AdaptiveBatchingEnabled);
        import.LargeTableThresholdBytes.ShouldBe(shared.LargeTableThresholdBytes);
        import.LargeTableRowThreshold.ShouldBe(shared.LargeTableRowThreshold);
        import.LargeTableBatchSize.ShouldBe(shared.LargeTableBatchSize);
        import.MaxBatchBytes.ShouldBe(shared.MaxBatchBytes);
        import.ValidationCommandTimeout.ShouldBe(shared.ImportValidationCommandTimeout);
        import.BulkCopyTimeout.ShouldBe(shared.ImportBulkCopyTimeout);
        import.Progress.ShouldBeSameAs(shared.Progress);
        import.SchemaDeploymentMode.ShouldBe(shared.SchemaDeploymentMode);
        import.DacpacDeploymentOptions.ShouldBeSameAs(shared.DacpacDeploymentOptions);
    }

    [Fact]
    public async Task Exporter_ExistingPackageWithoutOverwrite_FailsBeforeOpeningSqlServer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zsb-existing-{Guid.NewGuid():N}.sqlite");
        await File.WriteAllTextAsync(path, "existing");
        try
        {
            var exception = await Should.ThrowAsync<BridgeException>(() =>
                new SqlDataBridgeExporter().ExportAsync(
                    "Server=invalid;Database=invalid;User Id=invalid;Password=invalid;",
                    path));

            exception.Message.ShouldContain("already exists");
            ((await File.ReadAllTextAsync(path))).ShouldBe("existing");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MethodInfo? FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        return type.GetMethod(name, parameterTypes);
    }
}
