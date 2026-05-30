# Versioning

Zachtbeer.SqlDataBridge is currently published as pre-release builds. While it is pre-release, the public API and the SQLite package format may still change between releases. Pin a specific version and review the release notes before upgrading. Once a stable 1.0.0 release ships, the NuGet package will follow Semantic Versioning, and the policies below describe how that will work.

## Public API

Public types and members in the `Zachtbeer.SqlDataBridge` namespace are the intended supported API surface. Before 1.0.0 they may change without a major version bump. From 1.0.0 onward, breaking API changes require a new major version.

Internal types, metadata table implementation details, and test harness APIs are not public API.

## Package Format

SQLite packages include a package format version. Import validates that the package was produced by a supported format before copying data.

While pre-release, the package format may change between versions, so regenerate packages after upgrading rather than relying on older pre-release packages. From the first stable release onward:

- Patch and minor releases within a major version should continue to read packages produced by that major version.
- Unsupported future package versions fail with a clear `BridgeException`.
- Package metadata may gain additive fields in minor releases when existing import behavior remains compatible.
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
