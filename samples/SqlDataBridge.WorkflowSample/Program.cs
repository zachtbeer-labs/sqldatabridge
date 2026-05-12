using Zachtbeer.SqlDataBridge;

var sourceConnectionString = RequireEnvironmentVariable("ZSB_SOURCE_SQLSERVER");
var targetConnectionString = RequireEnvironmentVariable("ZSB_TARGET_SQLSERVER");
var sqlitePath = Environment.GetEnvironmentVariable("ZSB_PACKAGE_PATH") ?? "workflow-sample.sqlite";

var progress = new Progress<BridgeProgress>(p =>
{
    var table = string.IsNullOrWhiteSpace(p.TableName) ? "all tables" : p.TableName;
    Console.WriteLine($"{p.Kind}: {table} {p.RowsProcessed}/{p.TotalRows}");
});

var exportOptions = new ExportOptions
{
    TableSelection = ExportTableSelectionMode.Only,
    Tables = ["dbo.*"],
    ExcludeColumns = [],
    OverwriteExistingPackage = true,
    BatchSize = 1_000,
    CommandTimeout = 120,
    Progress = progress
};

var importOptions = new ImportOptions
{
    BatchSize = 1_000,
    ValidationCommandTimeout = 120,
    BulkCopyTimeout = 300,
    Progress = progress
};

try
{
    var exportPreflight = await new SqlDataBridgeExporter()
        .PreflightAsync(sourceConnectionString, exportOptions);
    if (!exportPreflight.IsValid)
    {
        PrintErrors("Export preflight failed", exportPreflight.Errors);
        return;
    }

    var exportResult = await new SqlDataBridgeExporter()
        .ExportAsync(sourceConnectionString, sqlitePath, exportOptions);
    PrintWarnings(exportResult.Warnings);
    Console.WriteLine($"Exported {exportResult.RowCount} rows from {exportResult.TableCount} tables to {sqlitePath}.");

    var manifest = await new DataPackageReader().ReadManifestAsync(sqlitePath);
    Console.WriteLine("SQLite file contents:");
    foreach (var table in manifest.Tables)
    {
        Console.WriteLine($"- {table.FullName}: {table.ExportedRowCount} rows, {table.Columns.Count} columns");
    }

    var importPreflight = await new SqlDataBridgeImporter()
        .PreflightAsync(sqlitePath, targetConnectionString, importOptions);
    if (!importPreflight.IsValid)
    {
        PrintErrors("Import preflight failed", importPreflight.Errors);
        return;
    }

    var importResult = await new SqlDataBridgeImporter()
        .ImportAsync(sqlitePath, targetConnectionString, importOptions);
    PrintWarnings(importResult.Warnings);
    Console.WriteLine($"Imported {importResult.RowCount} rows into {importResult.TableCount} target tables.");
}
catch (BridgeException exception)
{
    Console.Error.WriteLine("SqlDataBridge failed:");
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}

static string RequireEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Set the {name} environment variable before running the sample.");
    }

    return value;
}

static void PrintErrors(string title, IReadOnlyList<string> errors)
{
    Console.Error.WriteLine(title + ":");
    foreach (var error in errors)
    {
        Console.Error.WriteLine("- " + error);
    }
}

static void PrintWarnings(IReadOnlyList<string> warnings)
{
    foreach (var warning in warnings)
    {
        Console.WriteLine("Warning: " + warning);
    }
}
