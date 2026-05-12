# Contributing

Thanks for helping improve Zachtbeer.SqlDataBridge.

## Reporting Bugs / Requesting Features

Use [GitHub Issues](https://github.com/zachtbeer-labs/sqldatabridge/issues). For bugs, include:

- What you expected to happen.
- What actually happened.
- SQL Server version and .NET version.
- A minimal reproduction if possible.

## Development Setup

- Install the .NET SDKs needed by the solution.
- Install Docker for integration tests.
- Restore and build with:

```bash
dotnet restore SqlDataBridge.sln
dotnet build SqlDataBridge.sln
```

## Tests

Run the full suite with:

```bash
dotnet test SqlDataBridge.sln
```

Integration tests use Testcontainers to start SQL Server. If Docker is unavailable, run the unit test project directly:

```bash
dotnet test tests/SqlDataBridge.Tests/SqlDataBridge.Tests.csproj
```

## API Docs

Generate API reference metadata with:

```bash
dotnet tool restore
dotnet tool run docfx metadata docfx.json
```

## Pull Requests

- Keep public API changes deliberate and covered by `ApiShapeTests`.
- Add integration coverage for SQL Server behavior changes.
- Describe what you changed and why.
- Make sure CI passes before requesting review.
- Update `README.md` and `CHANGELOG.md` for user-visible changes.
- Do not commit generated packages from `bin`, `obj`, or `packages`.
- Keep issues and pull requests focused on one behavior or scenario.

## Code Style

- Function parameters, constructor arguments, and record definitions should stay on one line when practical.
- Keep things simple. This is a focused library, not a framework.
