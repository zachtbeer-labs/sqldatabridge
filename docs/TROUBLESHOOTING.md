# Troubleshooting

This page lists common SqlDataBridge failures and the action that usually fixes them.

## Import failed: target table is not empty

Import only writes into empty target tables. If you see an error that a target table must be empty, truncate or recreate the target table before import.

SqlDataBridge does this intentionally so partial merges, duplicate rows, and identity conflicts are explicit application decisions instead of implicit library behavior.

## Import failed: target table does not exist

SqlDataBridge does not create SQL Server schemas. Create or migrate the target schema before import, then run import again.

The target table names and schemas must match the exported source metadata.

## Import failed: target column does not exist

Every exported column must exist in the target table. Either add the missing target column or exclude the source column during export:

```csharp
var options = new ExportOptions
{
    ExcludeColumns = ["dbo.Customers.LegacyCode"]
};
```

## Import failed: extra target column is required

Extra target columns are allowed only when they are nullable, computed, identity, or have a default. If import fails on an extra target column, make that column nullable, add a default constraint, or exclude the table from the export scope.

## Export failed: unsupported included type

Export fails during preflight when an included column uses an unsupported SQL Server type. Exclude the column or exclude the table:

```csharp
var options = new ExportOptions
{
    ExcludeColumns =
    [
        "dbo.Payloads.PayloadVariant"
    ]
};
```

Unsupported v1 types are listed in the README.

`xml` columns are supported as text-preserved values. If a package is edited and an XML value becomes invalid, import fails with SQL Server's XML conversion error.

Native SQL Server `json` columns are supported as text-preserved values on SQL Server 2025 and Azure SQL. Existing JSON stored in text columns is handled as ordinary text. If a package is edited and a native JSON value becomes invalid, import fails with SQL Server's JSON validation error.

## Export failed: package file already exists

Export does not replace an existing SQLite package by default. Delete the file yourself or opt in to replacement:

```csharp
var options = new ExportOptions
{
    OverwriteExistingPackage = true
};
```

Replacement happens only after a successful export. Failed exports should not replace the previous package.

## Import failed: package format is not supported

Import validates the package format version before reading data. If a package was produced by an unsupported future version, use a compatible SqlDataBridge package version to import it.

If format metadata is missing, recreate the package with Zachtbeer.SqlDataBridge v1.0 or later.

## Dacpac deployment blocks possible data loss

Dacpac deployment blocks possible data loss by default. Review the target database and only opt out when that risk is acceptable:

```csharp
var options = new ImportOptions
{
    SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
    DacpacDeploymentOptions = new DacpacDeploymentOptions
    {
        BlockOnPossibleDataLoss = false
    }
};
```

Object drops are still disabled unless `AllowObjectDrops` is also set.

## Dacpac deployment skips users or permissions

Users, logins, permissions, and role membership are excluded by default so imports do not unexpectedly change target security. Opt in only when the target environment should receive those objects from the dacpac:

```csharp
var options = new ImportOptions
{
    SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
    DacpacDeploymentOptions = new DacpacDeploymentOptions
    {
        DeployUsers = true,
        DeployPermissions = true,
        DeployRoleMembership = true
    }
};
```

## Dacpac deployment should reject incompatible platform

By default, Zachtbeer.SqlDataBridge allows incompatible source and target platforms because DacFx platform checks are often stricter than practical SQL Server deployment needs. If your environment should enforce platform compatibility, opt out:

```csharp
var options = new ImportOptions
{
    SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac,
    DacpacDeploymentOptions = new DacpacDeploymentOptions
    {
        AllowIncompatiblePlatform = false
    }
};
```

## SQL Server certificate or trust errors

Local and containerized SQL Server instances often require `TrustServerCertificate=True` in development connection strings:

```text
Server=localhost;Database=MyDb;User Id=sa;Password=...;TrustServerCertificate=True
```

Use production-appropriate certificate validation for real deployments.

## Testcontainers or Docker failures

Integration tests require Docker and use SQL Server Testcontainers. Check that:

- Docker is running.
- The current user can start containers.
- Enough memory is available for SQL Server.
- No corporate proxy or registry policy blocks container image pulls.

If Docker is unavailable, run only the unit test project:

```bash
dotnet test tests/SqlDataBridge.Tests/SqlDataBridge.Tests.csproj
```

## NuGet vulnerability-data warnings

Warnings like `NU1900` can appear when the NuGet vulnerability service cannot be reached. They do not necessarily mean restore failed. If restore or build fails with `NU1301`, confirm network access to `https://api.nuget.org/v3/index.json`.
