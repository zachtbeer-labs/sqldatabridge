# Release Checklist

Use this checklist for each NuGet release.

1. Confirm `README.md`, `docs/API.md`, and `docs/SUPPORT_MATRIX.md` match the release.
2. Commit all release notes and code changes.
3. Run:

```bash
dotnet restore SqlDataBridge.sln
dotnet build SqlDataBridge.sln --configuration Release --no-restore
dotnet test SqlDataBridge.sln --configuration Release --no-build
dotnet tool restore
dotnet tool run docfx metadata docfx.json
dotnet pack src/SqlDataBridge/SqlDataBridge.csproj --configuration Release --no-build --output release-packages /p:Version=1.0.0 /p:PackageVersion=1.0.0 /p:InformationalVersion=1.0.0
```

4. Inspect the package contents:

```bash
dotnet nuget locals all --clear
```

Install the produced package into a throwaway app and run the README quickstart against test databases.

5. In GitHub Actions, run `Publish NuGet` manually and enter the package version.
6. Confirm the workflow uploaded package files and published the package to NuGet.org.
7. Create and push a signed version tag, for example `v1.0.0`, after the package is visible on NuGet.org.
