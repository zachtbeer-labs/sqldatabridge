# Changelog

All notable changes to Zachtbeer.SqlDataBridge are documented in this file.

## 1.0.0 - Unreleased

- Target `net8.0` and `net10.0`.
- Add NuGet package metadata, README packaging, XML documentation, symbols, and SourceLink.
- Add package format version metadata and import validation.
- Add adaptive import/export batching based on estimated table rows and data size.
- Add progress reporting, preflight validation, command timeout options, and package manifest inspection APIs.
- Protect existing package files by default with explicit `OverwriteExistingPackage` opt-in.
- Write exports through a temporary SQLite file before replacing the destination package.
- Document supported SQL Server types, target schema requirements, and known v1 behavior.
- Support `rowversion` / `timestamp` columns: export captures the bytes as `BLOB` (informational), import skips them so SQL Server generates fresh values. Both sides emit a warning.
- Add GitHub Actions CI and release workflows.
- Stamp the source SQL Server engine edition on exports (new `source_engine_edition` column on `zsb_schema_packages`) so import-time cross-platform decisions can use a real source signal instead of guessing from target-side probes. Bumps package format version to 4 — packages produced by 1.0.0-preview builds before this change cannot be imported by 1.0.0 final.
- Add `DacpacDeploymentOptions.AdaptAzureSourceForOnPremTarget` (default `true`). When deploying an Azure SQL-extracted dacpac to a non-Azure target, the dacpac's `model.xml` is rewritten on a temp copy: the optional `Containment` property is removed and contained / external `SqlUser` elements (`AuthenticationType` 2 and 4) are converted to `IsWithoutLogin=True` so DacFx no longer scripts `SET CONTAINMENT = PARTIAL` (Msg 12824) as a prerequisite. Set to `false` to deploy the source model verbatim.
- `BridgePackageManifest.SourceEngineEdition` exposes the stamp for previews and preflight diagnostics; preflight raises an error when an Azure-source package is imported with the adaptation flag disabled.
