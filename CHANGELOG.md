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
- Add GitHub Actions CI and release workflows.
