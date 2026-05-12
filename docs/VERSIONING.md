# Versioning

Zachtbeer.SqlDataBridge follows Semantic Versioning for the NuGet package.

## Public API

Public types and members in the `Zachtbeer.SqlDataBridge` namespace are treated as the supported API surface. Breaking API changes require a new major version.

Internal types, metadata table implementation details, and test harness APIs are not public API.

## Package Format

SQLite packages include a package format version. Import validates that the package was produced by a supported format before copying data.

For v1:

- v1.x package releases should continue to read v1 packages.
- Unsupported future package versions fail with a clear `BridgeException`.
- Package metadata may gain additive fields in minor releases when existing v1 import behavior remains compatible.
- Breaking package format changes require a new major version.

## Target Frameworks

The package currently targets:

- `net8.0`
- `net10.0`

Framework support may be expanded in minor releases when it does not break existing users. Dropping a supported target framework requires a new major version unless the framework itself is out of support and continued support is impractical.

## Dependencies

Dependency updates may happen in patch or minor releases when they preserve the public API and expected behavior. Dependency changes that force application code changes are treated as breaking changes.

## Release Versions

Release packages are built by GitHub Actions with an explicit version input. The package version, assembly version metadata, and NuGet package version are stamped during the release workflow.
