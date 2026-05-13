using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge;

/// <summary>
/// Imports a Zachtbeer.SqlDataBridge SQLite package into compatible SQL Server target tables.
/// </summary>
public sealed class SqlDataBridgeImporter
{
    /// <summary>
    /// Imports a SQLite package into empty compatible SQL Server target tables.
    /// </summary>
    /// <param name="sqliteFilePath">The SQLite package path.</param>
    /// <param name="sqlServerConnectionString">The SQL Server target connection string.</param>
    /// <param name="options">The import options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A summary of imported tables and rows.</returns>
    public async Task<BridgeResult> ImportAsync(string sqliteFilePath, string sqlServerConnectionString,
        ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ImportOptions();
        BatchPlanner.Validate(options);
        options.Progress?.Report(new BridgeProgress(BridgeProgressKind.OperationStarted, Message: "Import started."));

        var sqliteBuilder = new SqliteConnectionStringBuilder
            { DataSource = sqliteFilePath, Mode = SqliteOpenMode.ReadOnly };
        await using var sqlite = new SqliteConnection(sqliteBuilder.ConnectionString);
        await sqlite.OpenAsync(cancellationToken);

        await SqlitePackage.ValidateForImportAsync(sqlite, cancellationToken);

        switch (options.SchemaDeploymentMode)
        {
            case SchemaDeploymentMode.None:
                break;
            case SchemaDeploymentMode.DeployDacpac:
                var schemaPackage = await SqlitePackage.ReadSchemaPackageAsync(sqlite, cancellationToken);
                if (schemaPackage is null)
                {
                    throw new BridgeException(
                        "SQLite package does not contain a dacpac schema package. Export with SchemaCaptureMode.Dacpac before deploying schema during import.");
                }

                await DacpacSchemaManager.DeployAsync(sqlServerConnectionString, schemaPackage,
                    options.DacpacDeploymentOptions, allowDacpacObjectDrops: false, cancellationToken);
                break;
            default:
                throw new BridgeException($"SchemaDeploymentMode '{options.SchemaDeploymentMode}' is not supported.");
        }

        var tables = await SqlitePackage.ReadTablesAsync(sqlite, cancellationToken);
        var importOrder = await SqlitePackage.ReadImportOrderAsync(sqlite, cancellationToken);
        var packageWarnings = await SqlitePackage.ReadWarningsAsync(sqlite, cancellationToken);
        var warnings = new List<string>(packageWarnings);

        await SqlServerSchemaReader.ValidateImportTargetAsync(sqlServerConnectionString, tables,
            options.ValidationCommandTimeout, cancellationToken);
        ReportWarnings(options.Progress, warnings);

        await using var sqlServer = new SqlConnection(sqlServerConnectionString);
        await sqlServer.OpenAsync(cancellationToken);

        long totalRows = 0;
        foreach (var name in importOrder)
        {
            var table = tables.Single(t =>
                string.Equals(t.Name.FullName, name.FullName, StringComparison.OrdinalIgnoreCase));
            var expected = await SqlitePackage.ReadExpectedRowCountAsync(sqlite, table, cancellationToken);
            var batchSize = BatchPlanner.GetEffectiveBatchSize(options, expected, table.EstimatedSourceBytes);
            if (options.AdaptiveBatchingEnabled)
            {
                batchSize = Math.Min(batchSize, table.ExportBatchSize);
            }

            if (options.AdaptiveBatchingEnabled && batchSize < options.BatchSize)
            {
                AddWarning(warnings,
                    $"Adaptive batching set import batch size for '{table.Name.FullName}' to {batchSize} rows.",
                    options.Progress);
            }

            options.Progress?.Report(new BridgeProgress(BridgeProgressKind.TableStarted, table.Name.FullName,
                TotalRows: expected));
            var rows = await ImportTableAsync(sqlite, sqlServer, table, batchSize, options.BulkCopyTimeout,
                options.Progress, expected, cancellationToken);
            if (rows != expected)
            {
                throw new BridgeException(
                    $"Imported row count for '{table.Name.FullName}' was {rows}, expected {expected}.");
            }

            totalRows += rows;
            options.Progress?.Report(new BridgeProgress(BridgeProgressKind.TableCompleted, table.Name.FullName, rows,
                expected));
        }

        options.Progress?.Report(new BridgeProgress(BridgeProgressKind.OperationCompleted, RowsProcessed: totalRows,
            TotalRows: totalRows, Message: "Import completed."));
        return new BridgeResult(tables.Count, totalRows, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Imports a SQLite package into SQL Server using shared compatibility options.
    /// </summary>
    /// <param name="sqliteFilePath">The SQLite package path.</param>
    /// <param name="sqlServerConnectionString">The SQL Server target connection string.</param>
    /// <param name="options">The shared options to map to import options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A summary of imported tables and rows.</returns>
    public Task<BridgeResult> ImportAsync(string sqliteFilePath, string sqlServerConnectionString,
        BridgeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ImportAsync(sqliteFilePath, sqlServerConnectionString, options.ToImportOptions(), cancellationToken);
    }

    /// <summary>
    /// Validates an import without deploying schema or copying rows.
    /// </summary>
    /// <param name="sqliteFilePath">The SQLite package path.</param>
    /// <param name="sqlServerConnectionString">The SQL Server target connection string.</param>
    /// <param name="options">The import options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The preflight validation result.</returns>
    public async Task<BridgePreflightResult> PreflightAsync(string sqliteFilePath, string sqlServerConnectionString,
        ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ImportOptions();
        var errors = new List<string>();
        try
        {
            BatchPlanner.Validate(options);
            var sqliteBuilder = new SqliteConnectionStringBuilder
                { DataSource = sqliteFilePath, Mode = SqliteOpenMode.ReadOnly };
            await using var sqlite = new SqliteConnection(sqliteBuilder.ConnectionString);
            await sqlite.OpenAsync(cancellationToken);

            await SqlitePackage.ValidateForImportAsync(sqlite, cancellationToken);
            var manifest = await SqlitePackage.ReadManifestAsync(sqlite, cancellationToken);
            if (options.SchemaDeploymentMode == SchemaDeploymentMode.DeployDacpac && !manifest.ContainsDacpac)
            {
                errors.Add(
                    "SQLite package does not contain a dacpac schema package. Export with SchemaCaptureMode.Dacpac before deploying schema during import.");
            }

            if (options.SchemaDeploymentMode == SchemaDeploymentMode.DeployDacpac
                && manifest.DacpacSchemaScope == DacpacSchemaScope.SelectedExportTables
                && options.DacpacDeploymentOptions.AllowObjectDrops)
            {
                errors.Add("DacpacDeploymentOptions.AllowObjectDrops cannot be used with a selected-table dacpac schema package because unrelated target objects would be compared against a reduced source model.");
            }

            var tables = await SqlitePackage.ReadTablesAsync(sqlite, cancellationToken);
            try
            {
                await SqlServerSchemaReader.ValidateImportTargetAsync(sqlServerConnectionString, tables,
                    options.ValidationCommandTimeout, cancellationToken);
            }
            catch (BridgeException exception)
            {
                errors.Add(exception.Message);
            }
            catch (SqlException exception)
            {
                errors.Add(exception.Message);
            }

            var warnings = BuildImportWarnings(tables, manifest.Warnings, options);
            return new BridgePreflightResult(errors.Count == 0, errors, warnings, manifest);
        }
        catch (BridgeException exception)
        {
            errors.Add(exception.Message);
        }
        catch (SqlException exception)
        {
            errors.Add(exception.Message);
        }

        return new BridgePreflightResult(false, errors, Array.Empty<string>(), null);
    }

    private static async Task<long> ImportTableAsync(SqliteConnection sqlite, SqlConnection sqlServer,
        TableMetadata table, int batchSize, int? bulkCopyTimeout, IProgress<BridgeProgress>? progress,
        long expectedRows, CancellationToken cancellationToken)
    {
        var columns = table.ExportedColumns;
        var sqliteColumns = string.Join(", ", columns.Select(c => BridgeIdentifier.QuoteSqliteName(c.Name)));

        await using var select = sqlite.CreateCommand();
        select.CommandText = $"SELECT {sqliteColumns} FROM {BridgeIdentifier.QuoteSqliteName(table.SqliteTableName)}";
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);

        var rows = 0L;
        using var bulk = new SqlBulkCopy(sqlServer,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.UseInternalTransaction, null)
        {
            DestinationTableName = BridgeIdentifier.QuoteSqlServerTable(table.Name),
            BatchSize = batchSize,
            EnableStreaming = true
        };
        if (bulkCopyTimeout.HasValue)
        {
            bulk.BulkCopyTimeout = bulkCopyTimeout.Value;
        }

        foreach (var column in columns)
        {
            bulk.ColumnMappings.Add(column.Name, column.Name);
        }

        var projectingReader = new SqliteCoercingDataReader(reader, columns, () =>
        {
            rows++;
            if (rows % batchSize == 0)
            {
                progress?.Report(new BridgeProgress(BridgeProgressKind.RowsCopied, table.Name.FullName, rows,
                    expectedRows));
            }
        });
        await bulk.WriteToServerAsync(projectingReader, cancellationToken);
        if (rows == 0 || rows % batchSize != 0)
        {
            progress?.Report(new BridgeProgress(BridgeProgressKind.RowsCopied, table.Name.FullName, rows,
                expectedRows));
        }

        return rows;
    }

    private static IReadOnlyList<string> BuildImportWarnings(IReadOnlyList<TableMetadata> tables,
        IReadOnlyList<string> packageWarnings, ImportOptions options)
    {
        var warnings = new List<string>(packageWarnings);
        foreach (var table in tables)
        {
            var batchSize =
                BatchPlanner.GetEffectiveBatchSize(options, table.EstimatedSourceRowCount, table.EstimatedSourceBytes);
            if (options.AdaptiveBatchingEnabled)
            {
                batchSize = Math.Min(batchSize, table.ExportBatchSize);
            }

            if (options.AdaptiveBatchingEnabled && batchSize < options.BatchSize)
            {
                warnings.Add(
                    $"Adaptive batching set import batch size for '{table.Name.FullName}' to {batchSize} rows.");
            }
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddWarning(List<string> warnings, string warning, IProgress<BridgeProgress>? progress)
    {
        if (!warnings.Contains(warning, StringComparer.Ordinal))
        {
            warnings.Add(warning);
            progress?.Report(new BridgeProgress(BridgeProgressKind.Warning, Message: warning));
        }
    }

    private static void ReportWarnings(IProgress<BridgeProgress>? progress, IReadOnlyList<string> warnings)
    {
        if (progress is null)
        {
            return;
        }

        foreach (var warning in warnings.Distinct(StringComparer.Ordinal))
        {
            progress.Report(new BridgeProgress(BridgeProgressKind.Warning, Message: warning));
        }
    }
}
