# API Reference

SqlDataBridge generates API reference metadata from XML comments with DocFX.

Start with these public entry points:

- `SqlDataBridgeExporter` exports SQL Server table data into a local data package.
- `SqlDataBridgeImporter` validates and imports a data package into SQL Server.
- `SqlDataBridge` provides static `ExportAsync` and `ImportAsync` helpers for simple calls.

Support types live in `Zachtbeer.SqlDataBridge.Models`:

- `DataPackageReader` reads package manifests without importing rows.
- `ExportOptions` and `ImportOptions` configure table selection, export filters, batching, progress, timeouts, data table naming, and optional dacpac behavior.

`Zachtbeer.SqlDataBridge.Models.ExportOptions.DataTablePrefix` controls the prefix used for exported SQLite data tables. It defaults to `zsb_data`, so `dbo.Customers` becomes `zsb_data_dbo__customers`; set it to `null` or an empty string to omit the prefix.

`Zachtbeer.SqlDataBridge.Models.ExportOptions.Tables` supports exact source table names and `*` wildcards, for example `dbo.Customers`, `Customers`, `dbo.zz*`, and `*.zz*`. `TableSelection` controls whether those patterns are included (`Only`) or excluded (`AllExcept`, the default).

To regenerate the API metadata locally:

```bash
dotnet tool restore
dotnet tool run docfx metadata docfx.json
```

The generated metadata is written to [docs/api](api/). Other public models include:

- `BridgeOptions`
- `ExportTableSelectionMode`
- `GlobalWhereClause`
- `PerTableWhereClause`
- `BridgeResult`
- `BridgePreflightResult`
- `BridgePackageManifest`
- `BridgeProgress`
