# Architecture

SqlDataBridge moves SQL Server table data through a self-describing SQLite file.

The normal flow is:

1. Read SQL Server metadata.
2. Export selected table rows into a temporary SQLite file.
3. Store source metadata, row counts, warnings, and import order in the same file.
4. Move the completed file into place.
5. Validate a target SQL Server database before import.
6. Bulk-copy rows into empty target tables.

By default, the library moves data and metadata. It does not create or migrate the target schema unless the caller explicitly uses dacpac capture during export and dacpac deployment during import.

## Export Flow

The exporter plans before it copies rows. It resolves table patterns with `ExportTableSelectionMode`, validates explicit column exclusions, skips computed columns, rejects unsupported included SQL Server types, and computes a schema hash for informational metadata.

After planning succeeds, export writes to a temporary SQLite file in the destination directory. The temporary file is moved to the requested package path only after all table data and row counts are written successfully. Existing packages are not replaced unless the caller opts in with `OverwriteExistingPackage`.

Export preflight runs the same planning and validation steps without creating a SQLite file or copying rows. Long-running exports can report progress through `IProgress<BridgeProgress>` and can use an optional SQL command timeout for metadata and data reads.

## SQLite package

The SQLite package is both the data package and the manifest. It contains two kinds of tables:

- `zsb_*` metadata tables used by SqlDataBridge
- data tables containing exported source rows, named with `ExportOptions.DataTablePrefix` (`zsb_data` by default)

Metadata includes package version, package format version, source tables, source columns, skipped tables/columns, warnings, expected row counts, estimated source row counts, estimated source data bytes, effective export batch sizes, schema hash, and import order.

The SQLite file is intended to be inspected and transported, but the metadata schema is owned by SqlDataBridge. External tools should treat it as a versioned package format rather than a general-purpose database schema contract.

Applications can read supported package metadata through `DataPackageReader.ReadManifestAsync` instead of querying `zsb_*` tables directly.

## Type Conversion

Export stores SQL Server values using SQLite affinities chosen for stable transport:

- integer-like values use `INTEGER`
- floating-point values use `REAL`
- binary values use `BLOB`
- date/time, decimal, money, GUID, and text values use `TEXT` where that preserves roundtrip behavior more predictably

The original SQL Server type metadata is stored in `zsb_columns` and used during import to coerce SQLite values back into values accepted by `SqlBulkCopy`.

## Import Flow

The importer opens the SQLite package read-only, validates required metadata tables, checks the package format version, reads table/column/sizing metadata, then validates the target SQL Server schema before copying rows.

Target validation checks that:

- each target table exists
- each exported column exists in the target table
- extra target columns are nullable, computed, identity, or defaulted
- target tables are empty before import begins

Rows are imported with `SqlBulkCopy`, using identity preservation so foreign-key relationships and identity values can roundtrip when the target schema is compatible.

Import preflight runs package and target validation without deploying dacpacs or copying rows. Long-running imports can report progress through `IProgress<BridgeProgress>` and can use optional validation and bulk-copy timeouts.

## Dacpac Deployment Safety

Dacpac deployment defaults are intentionally conservative around destructive changes. DacFx blocks possible data loss, does not drop target objects missing from the package schema, excludes database files and filegroups, and excludes users, logins, permissions, and role membership unless callers opt in. Incompatible SQL Server platforms are allowed by default because DacFx platform checks are often stricter than practical SQL Server deployment needs.

The library stores dacpac packages in the SQLite package. It does not store SQL schema scripts or execute generated SQL scripts.

## Import Ordering

The export plan reads SQL Server foreign keys among selected tables and builds an import order that places referenced tables before dependent tables. This keeps common parent/child imports compatible with target foreign-key constraints.

Cycles and complex constraint scenarios should be handled by preparing the target schema appropriately before import.

## Why SQLite

SQLite gives the package a single-file format with broad tooling support, transactional writes, simple metadata storage, and enough type flexibility for transport. It also lets humans and AI coding agents inspect exported data without requiring SQL Server access.

SqlDataBridge uses SQLite as a transport container, not as a replacement for SQL Server semantics.
