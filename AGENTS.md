# Repository Guidelines

## Purpose For Coding Agents

This file is for agentic coding tools working in this repository. Prefer repo-grounded changes over generic .NET advice. This library is not live yet; until the NuGet package is published, do not prioritize backwards compatibility for public API changes. Prefer the clearest API and implementation. Remove this note once the package is live.

## Main Files And Responsibilities

- `src/SqlDataBridge/SqlDataBridgeExporter.cs`: top-level export workflow from SQL Server to SQLite, including overwrite safety, progress, batching, and optional dacpac capture.
- `src/SqlDataBridge/SqlDataBridgeImporter.cs`: top-level import workflow from SQLite back to SQL Server, including package validation, schema deployment, target validation, and bulk copy.
- `src/SqlDataBridge/SqlDataBridgeOptions.cs`: public options, defaults, enums, and compatibility surface. Treat default changes as product decisions.
- `src/SqlDataBridge/BridgeOperationalModels.cs`: public result, manifest, progress, and exception types.
- `src/SqlDataBridge/DataPackageReader.cs`: supported read-only metadata access for package consumers.
- `src/SqlDataBridge/Internal/SqlServerSchemaReader.cs`: SQL Server metadata discovery, export plan creation, import target validation, table/column filtering, and WHERE handling.
- `src/SqlDataBridge/Internal/SqlitePackage.cs`: SQLite package schema, manifest storage, validation, import order, warnings, and dacpac payload persistence.
- `src/SqlDataBridge/Internal/DacpacSchemaManager.cs`: DacFx extract/deploy behavior and schema-scope safety rules.
- `src/SqlDataBridge/Internal/ValueConverter.cs` and `SqliteCoercingDataReader.cs`: SQL Server to SQLite type conversion and import-time coercion.
- `src/SqlDataBridge/Internal/BatchPlanner.cs`, `ImportPlanner.cs`, and `BridgeIdentifier.cs`: batching rules, dependency/import ordering, and SQL identifier quoting.

## Common Work Paths

For export behavior, start in `SqlDataBridgeExporter`, then inspect `SqlServerSchemaReader`, `SqlitePackage`, `BatchPlanner`, and `ValueConverter`. For import behavior, start in `SqlDataBridgeImporter`, then inspect `SqlitePackage`, `SqlServerSchemaReader.ValidateImportTargetAsync`, `SqliteCoercingDataReader`, and `ImportPlanner`. For dacpac behavior, start with `DacpacSchemaManager`, `DacpacCaptureOptions`, `DacpacDeploymentOptions`, `DacpacSchemaScope`, and integration tests in `tests/SqlDataBridge.IntegrationTests/Tests/DacpacSchemaScopeTests.cs`.

For public API changes, update `SqlDataBridgeOptions.cs`, `BridgeOperationalModels.cs`, XML docs, `tests/SqlDataBridge.Tests/ApiShapeTests.cs`, README examples, and DocFX metadata when appropriate. Keep the package ID and namespace spelled `Zachtbeer.SqlDataBridge`.

## Tests And Verification

- `dotnet test tests/SqlDataBridge.Tests/SqlDataBridge.Tests.csproj`: fast unit/API-shape coverage.
- `SQLDATABRIDGE_SQLSERVER_IMAGE=mcr.microsoft.com/mssql/server:2025-latest dotnet test tests/SqlDataBridge.IntegrationTests/SqlDataBridge.IntegrationTests.csproj`: agent/local SQL Server container coverage; requires Docker. GitHub Actions runs the broader SQL Server version matrix.
- `dotnet test SqlDataBridge.sln`: full suite.
- `dotnet tool restore && dotnet tool run docfx metadata docfx.json`: regenerate API docs after public API or XML doc changes.
- Avoid running `dotnet pack` for now unless the user explicitly asks; it has been hanging in this workspace. When package verification is needed, ask before running it.

Name tests by behavior, for example `Export_ExistingPackageWithoutOverwrite_FailsWithoutReplacingPackage`. Add integration coverage for SQL Server, DacFx, type fidelity, schema deployment, and round-trip changes.

## Agent Safety Rules

Do not commit generated outputs from `bin`, `obj`, `release-packages`, `.nupkg`, or `.snupkg`. Do not commit connection strings, customer data, or generated SQLite packages. Use `DataPackageReader` for supported package inspection; do not encourage consumers to rely on internal `zsb_*` tables. Preserve export/import safety defaults unless the task explicitly changes them. When changing dacpac deployment, verify selected-table schema packages do not enable object-drop behavior that could affect unrelated target objects.
