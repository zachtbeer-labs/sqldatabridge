# SqlDataBridge

[![CI](https://github.com/zachtbeer-labs/sqldatabridge/actions/workflows/ci.yml/badge.svg)](https://github.com/zachtbeer-labs/sqldatabridge/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Zachtbeer.SqlDataBridge.svg)](https://www.nuget.org/packages/Zachtbeer.SqlDataBridge)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Target frameworks](https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512bd4.svg)](src/SqlDataBridge/SqlDataBridge.csproj)

Create portable, queryable SQL Server data packages for careful support, testing, and roundtrip import workflows.

SqlDataBridge is a .NET library for exporting selected SQL Server data into a single local package. The package can be opened with common SQLite tools, attached to a ticket, moved between machines, inspected by humans or AI coding agents, and imported into a prepared SQL Server database later.

It is built for application-controlled extracts, not for SQL Server backups, live replication, incremental sync, or general-purpose ETL.

## Use It For

- Send a small customer or production-like data snapshot with a support issue.
- Give an AI coding agent a local, queryable copy of relevant SQL Server tables instead of database credentials.
- Move selected data into a dev, test, QA, or demo SQL Server database.
- Package reproducible database state for bug reports and regression tests.
- Inspect SQL Server data on a machine that does not have SQL Server installed.
- Keep table data, row counts, type metadata, import order, warnings, and optional schema in one package.

## Capabilities At A Glance

- Export all tables or selected tables with include/exclude patterns.
- Exclude unsupported, sensitive, or noisy columns.
- Preserve SQL Server source names, type metadata, nullability, identity, computed-column, precision, scale, and collation details.
- Store expected row counts, estimated source sizes, warnings, and foreign-key-based import order.
- Preflight export and import before copying rows.
- Report progress during long exports and imports.
- Read a package manifest without importing it.
- Optionally capture SQL Server schema as a dacpac and deploy it before import.

## Install

```bash
dotnet add package Zachtbeer.SqlDataBridge
```

## Quickstart

```csharp
using Zachtbeer.SqlDataBridge;

var packagePath = "database.sqlite";

await SqlDataBridge.ExportAsync(sourceSqlServerConnectionString, packagePath);

await SqlDataBridge.ImportAsync(packagePath, targetSqlServerConnectionString);
```

Before importing, the target SQL Server tables must exist and be empty. If you want the SQLite file to carry schema too, export with dacpac capture and opt into dacpac deployment during import.

## Real-World Examples

In addition to `using Zachtbeer.SqlDataBridge;`, configured examples use the model types namespace:

```csharp
using Zachtbeer.SqlDataBridge.Models;
```

Every options type (`ExportOptions`, `ImportOptions`, `BridgeOptions`, `DacpacCaptureOptions`, `DacpacDeploymentOptions`) exposes a static `Default` property that returns a fresh, mutable instance pre-populated with the documented defaults. Use it as a discoverable starting point and tweak only what you need — each access returns a new instance, so mutating it never affects other callers.

Export only the tables needed to reproduce an issue:

```csharp
var options = ExportOptions.Default;
options.TableSelection = ExportTableSelectionMode.Only;
options.Tables =
[
    "dbo.Customers",
    "dbo.Invoices",
    "dbo.InvoiceLineItems"
];

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "billing-repro.sqlite",
    options);
```

`Tables` supports exact source table names and `*` wildcards. Use schema-qualified names such as `dbo.Customers`, table names such as `Customers`, or wildcard patterns such as `dbo.zz*` and `*.zz*`.

By default, table patterns are exclusions because `TableSelection` defaults to `AllExcept`:

```csharp
var options = ExportOptions.Default;
options.Tables = ["*.zz*"];

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "without-staging.sqlite",
    options);
```

Exclude unsupported, sensitive, or unhelpful columns:

```csharp
var options = ExportOptions.Default;
options.TableSelection = ExportTableSelectionMode.Only;
options.Tables = ["dbo.Payloads", "dbo.SupportCases"];
options.ExcludeColumns =
[
    "dbo.Payloads.RawXml",
    "dbo.SupportCases.InternalNotes"
];

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "support-snapshot.sqlite",
    options);
```

Customize the SQLite data table prefix when a package needs to fit another local naming convention:

```csharp
var options = ExportOptions.Default;
options.DataTablePrefix = "support_data";

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "support-snapshot.sqlite",
    options);
```

`DataTablePrefix` defaults to `zsb_data`, which creates data tables like `zsb_data_dbo__customers`. Set it to `null` or an empty string to omit the prefix and create names like `dbo__customers`. Metadata tables remain `zsb_*`.

Apply global WHERE predicates to any selected table that has a matching source column:

```csharp
var options = ExportOptions.Default;
options.GlobalWhereClauses =
[
    new GlobalWhereClause("TenantId", "TenantId = 123"),
    new GlobalWhereClause("Active", "Active = 1")
];

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "tenant-snapshot.sqlite",
    options);
```

Stack per-table WHERE predicates with global predicates for exact source tables:

```csharp
var options = ExportOptions.Default;
options.GlobalWhereClauses =
[
    new GlobalWhereClause("TenantId", "TenantId = 123")
];
options.PerTableWhereClauses =
[
    new PerTableWhereClause("dbo.Orders", "Status = 'Open'")
];

var result = await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "tenant-snapshot.sqlite",
    options);
```

Check an import before copying rows:

```csharp
var preflight = await new SqlDataBridgeImporter().PreflightAsync("support-snapshot.sqlite", targetSqlServerConnectionString);

if (!preflight.IsValid)
{
    foreach (var error in preflight.Errors)
    {
        Console.Error.WriteLine(error);
    }
}
```

Read what is inside a package:

```csharp
var manifest = await new DataPackageReader().ReadManifestAsync("support-snapshot.sqlite");

foreach (var table in manifest.Tables)
{
    Console.WriteLine($"{table.FullName}: {table.ExportedRowCount} rows");
}
```

## AI-Assisted Development Use Cases

SQLite is a useful handoff format for coding agents because it is local, inspectable, and does not require live database access.

Use SqlDataBridge to:

- give an agent selected SQL Server tables as a SQLite database extract
- let an agent inspect row counts, source table names, and SQL Server type metadata through the package manifest
- create reproducible database fixtures for generated tests
- share a scoped support/debug package without granting database credentials or network access

The package format is versioned and owned by SqlDataBridge. Use `DataPackageReader` for supported metadata reads instead of depending on internal `zsb_*` table shapes.

## Workflow Features

Report progress during a long export:

```csharp
var progress = new Progress<BridgeProgress>(p =>
{
    Console.WriteLine($"{p.Kind}: {p.TableName} {p.RowsProcessed}/{p.TotalRows}");
});

var options = ExportOptions.Default;
options.TableSelection = ExportTableSelectionMode.Only;
options.Tables = ["dbo.Payloads"];
options.CommandTimeout = 120;
options.Progress = progress;

await new SqlDataBridgeExporter().ExportAsync(
    sourceSqlServerConnectionString,
    "large-table.sqlite",
    options);
```

Tune batching for large or wide tables:

```csharp
var options = ExportOptions.Default;
options.BatchSize = 1_000;
options.LargeTableThresholdBytes = 50L * 1024 * 1024;
options.LargeTableBatchSize = 250;
options.MaxBatchBytes = 4L * 1024 * 1024;
```

Size-aware batching is enabled by default. `BatchSize` is the upper bound; adaptive batching only lowers it for large or wide tables.

## Schema Capture

By default, SqlDataBridge exports data and metadata, not SQL Server schema scripts.

To carry schema in the SQLite file, enable dacpac capture:

```csharp
var exportOptions = ExportOptions.Default;
exportOptions.SchemaCaptureMode = SchemaCaptureMode.Dacpac;
```

By default, dacpac capture includes the full source database schema. To capture only the tables selected by the export plan, set `DacpacCaptureOptions.SchemaScope = DacpacSchemaScope.SelectedExportTables`.

Then opt into dacpac deployment before import:

```csharp
var importOptions = ImportOptions.Default;
importOptions.SchemaDeploymentMode = SchemaDeploymentMode.DeployDacpac;
importOptions.DacpacDeploymentOptions.AllowIncompatiblePlatform = false;
```

`DacpacDeploymentOptions` is already initialized to its defaults on a fresh `ImportOptions`, so you can adjust its fields directly without replacing the object.

Dacpac deployment is conservative by default: possible data loss is blocked, target objects are not dropped, and users, logins, permissions, and role membership are skipped unless you explicitly enable them.

## What Gets Preserved

The SQLite file includes:

- one SQLite data table per exported SQL Server table
- SQL Server source schema, table, and column names
- SQL Server type metadata, including precision, scale, nullability, identity, computed, and collation metadata
- skipped tables and columns
- export row counts
- estimated source row counts and table sizes
- import order based on foreign keys between selected tables
- warnings produced during export
- optional dacpac schema payload

Values are stored using SQLite affinities chosen for reliable roundtrip behavior. For example, integer-like values use `INTEGER`, floating-point values use `REAL`, binary values use `BLOB`, and date/time, decimal, money, GUID, and text values use `TEXT` where that better preserves SQL Server behavior.

## Import Expectations

Import is intentionally strict:

- target tables must already exist unless dacpac deployment creates them
- target tables included in the SQLite file must be empty
- every exported column must exist in the target table
- extra target columns must be nullable, computed, identity, or have a default
- constraints stay enabled
- imported row counts are checked against exported row counts

Identity values are preserved with `SqlBulkCopyOptions.KeepIdentity`, so parent/child relationships can roundtrip when the target schema is compatible.

## Responsible Data Handling

SqlDataBridge is designed for scoped, application-controlled extracts. Select only the tables and rows needed for the task, exclude sensitive columns explicitly, and inspect the package manifest before sharing it outside your environment.

The package is local and portable by design. Treat it with the same care as any exported production data.

## Supported SQL Server Types

Supported types:

- Integer and boolean: `bigint`, `int`, `smallint`, `tinyint`, `bit`
- Floating point: `float`, `real`
- Text: `char`, `varchar`, `text`, `nchar`, `nvarchar`, `ntext`
- Date/time: `date`, `datetime`, `datetime2`, `datetimeoffset`, `smalldatetime`, `time`
- Numeric text-preserved values: `decimal`, `numeric`, `money`, `smallmoney`
- XML text-preserved values: `xml`
- JSON text-preserved values: native `json` columns on SQL Server 2025 and Azure SQL
- Binary: `binary`, `varbinary`, `image`
- Identifiers: `uniqueidentifier`
- Server-generated: `timestamp`, `rowversion`

Unsupported included types fail export preflight:

- `sql_variant`
- `geography`
- `geometry`
- `hierarchyid`

Exclude unsupported columns with `ExcludeColumns`.

`rowversion` / `timestamp` columns are captured as 8-byte `BLOB` values during export (for inspection) but skipped during import — SQL Server generates fresh values on the target. Export and import each emit a warning when one of these columns is present.

XML columns are stored as SQLite `TEXT` and imported back into SQL Server `xml` columns. If a package is edited and an XML value is no longer valid XML, import fails with SQL Server's XML conversion error.

Native SQL Server `json` columns are stored as SQLite `TEXT` and imported back into SQL Server `json` columns. Existing JSON stored in `nvarchar`, `varchar`, or other text columns is handled as ordinary text. If a package is edited and a native JSON value is no longer valid JSON, import fails with SQL Server's JSON validation error.

## Limits

Use another tool if you need:

- SQL Server-native backup and restore
- incremental sync
- merges or upserts into existing target rows
- complex transforms during import
- full schema migration without dacpac
- support for every SQL Server type

## Samples

- [Minimal sample](samples/SqlDataBridge.Sample): export a data package and import it into a prepared target SQL Server schema.
- [Workflow sample](samples/SqlDataBridge.WorkflowSample): run preflight checks, report progress, inspect the manifest, import rows, and print warnings/errors.

## Documentation

- [Troubleshooting](docs/TROUBLESHOOTING.md): common failures and fixes
- [Support matrix](docs/SUPPORT_MATRIX.md): supported frameworks, platforms, files, and v1 boundaries
- [Architecture](docs/ARCHITECTURE.md): export/import flow, package internals, type conversion, and dacpac behavior
- [API reference](docs/API.md): generated API metadata entry points
- [Versioning](docs/VERSIONING.md): compatibility policy
- [Release checklist](docs/RELEASE.md)
- [Changelog](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Security](SECURITY.md)

## Project Status

SqlDataBridge is prepared as a v1 NuGet package from Zachtbeer Labs B.V. The public API and SQLite file format are intended to stay stable across v1.x releases, with breaking changes reserved for a new major version.

## Running Tests

```bash
dotnet test SqlDataBridge.sln
```

Integration tests require Docker for SQL Server Testcontainers.
