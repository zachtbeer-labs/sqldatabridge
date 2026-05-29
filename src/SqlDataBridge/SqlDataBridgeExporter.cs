using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Zachtbeer.SqlDataBridge.Internal;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge;

/// <summary>
/// Exports SQL Server table data into a self-describing SQLite package.
/// </summary>
public sealed class SqlDataBridgeExporter
{
    /// <summary>
    /// Exports selected SQL Server user tables into a SQLite package.
    /// </summary>
    /// <param name="sqlServerConnectionString">The SQL Server source connection string.</param>
    /// <param name="sqliteFilePath">The destination SQLite package path.</param>
    /// <param name="options">The export options. When omitted, all user tables are exported.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A summary of exported tables, rows, and warnings.</returns>
    public async Task<BridgeResult> ExportAsync(string sqlServerConnectionString, string sqliteFilePath, ExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ExportOptions.Default;
        BatchPlanner.Validate(options);
        options.Progress?.Report(new BridgeProgress(BridgeProgressKind.OperationStarted, Message: "Export started."));

        var destinationPath = Path.GetFullPath(sqliteFilePath);
        if (File.Exists(destinationPath) && !options.OverwriteExistingPackage)
        {
            throw new BridgeException($"SQLite package '{destinationPath}' already exists. Set OverwriteExistingPackage to true to replace it after a successful export.");
        }

        var plan = await SqlServerSchemaReader.CreateExportPlanAsync(sqlServerConnectionString, options, cancellationToken);
        var warnings = BuildExportWarnings(plan, options);
        plan = plan with { Warnings = warnings };
        ReportWarnings(options.Progress, warnings);
        SchemaPackage? schemaPackage = options.SchemaCaptureMode switch
        {
            SchemaCaptureMode.None => null,
            SchemaCaptureMode.Dacpac => await DacpacSchemaManager.ExtractAsync(sqlServerConnectionString, plan, options.DacpacCaptureOptions, cancellationToken),
            _ => throw new BridgeException($"SchemaCaptureMode '{options.SchemaCaptureMode}' is not supported.")
        };
        var tempPath = CreateTemporaryPackagePath(destinationPath);
        var sqliteBuilder = new SqliteConnectionStringBuilder { DataSource = tempPath };
        await using var sqlite = new SqliteConnection(sqliteBuilder.ConnectionString);

        try
        {
            await sqlite.OpenAsync(cancellationToken);
            await SqlitePackage.InitializeAsync(sqlite, plan, cancellationToken);
            if (schemaPackage is not null)
            {
                await SqlitePackage.StoreSchemaPackageAsync(sqlite, schemaPackage, cancellationToken);
            }

            await using var sqlServer = new SqlConnection(sqlServerConnectionString);
            await sqlServer.OpenAsync(cancellationToken);

            long totalRows = 0;
            foreach (var table in plan.ImportOrder.Select(name => plan.Tables.Single(t => string.Equals(t.Name.FullName, name.FullName, StringComparison.OrdinalIgnoreCase))))
            {
                var batchSize = BatchPlanner.GetEffectiveBatchSize(options, table.EstimatedSourceRowCount, table.EstimatedSourceBytes);
                options.Progress?.Report(new BridgeProgress(BridgeProgressKind.TableStarted, table.Name.FullName, TotalRows: table.EstimatedSourceRowCount));
                var rows = await ExportTableAsync(sqlServer, sqlite, table, batchSize, options.CommandTimeout, options.Progress, cancellationToken);
                await SqlitePackage.RecordTableStatsAsync(sqlite, table, rows, batchSize, cancellationToken);
                totalRows += rows;
                options.Progress?.Report(new BridgeProgress(BridgeProgressKind.TableCompleted, table.Name.FullName, rows, rows));
            }

            await sqlite.CloseAsync();
            // Pooling keeps the sqlite3 file handle alive past CloseAsync/Dispose.
            // Evict it so the move/delete below can succeed on Windows.
            SqliteConnection.ClearPool(sqlite);
            File.Move(tempPath, destinationPath, options.OverwriteExistingPackage);

            options.Progress?.Report(new BridgeProgress(BridgeProgressKind.OperationCompleted, RowsProcessed: totalRows, TotalRows: totalRows, Message: "Export completed."));
            return new BridgeResult(plan.Tables.Count, totalRows, plan.Warnings);
        }
        catch
        {
            try { SqliteConnection.ClearPool(sqlite); } catch { /* best effort */ }
            DeleteTemporaryPackage(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Exports selected SQL Server user tables into a SQLite package using shared compatibility options.
    /// </summary>
    /// <param name="sqlServerConnectionString">The SQL Server source connection string.</param>
    /// <param name="sqliteFilePath">The destination SQLite package path.</param>
    /// <param name="options">The shared options to map to export options.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A summary of exported tables, rows, and warnings.</returns>
    public Task<BridgeResult> ExportAsync(
        string sqlServerConnectionString,
        string sqliteFilePath,
        BridgeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExportAsync(sqlServerConnectionString, sqliteFilePath, options.ToExportOptions(), cancellationToken);
    }

    /// <summary>
    /// Validates an export plan without creating a SQLite package or copying rows.
    /// </summary>
    /// <param name="sqlServerConnectionString">The SQL Server source connection string.</param>
    /// <param name="options">The export options. When omitted, all user tables are included.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The preflight validation result.</returns>
    public async Task<BridgePreflightResult> PreflightAsync(
        string sqlServerConnectionString,
        ExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= ExportOptions.Default;
        var errors = new List<string>();
        try
        {
            BatchPlanner.Validate(options);
            if (options.SchemaCaptureMode == SchemaCaptureMode.Dacpac)
            {
                var builder = new SqlConnectionStringBuilder(sqlServerConnectionString);
                if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                {
                    errors.Add("Schema capture requires a SQL Server connection string with a database name.");
                }
            }

            var plan = await SqlServerSchemaReader.CreateExportPlanAsync(sqlServerConnectionString, options, cancellationToken);
            var warnings = BuildExportWarnings(plan, options);
            plan = plan with { Warnings = warnings };
            return new BridgePreflightResult(errors.Count == 0, errors, warnings, SqlitePackage.CreatePlannedManifest(plan, options));
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

    private static string CreateTemporaryPackagePath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(
            string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void DeleteTemporaryPackage(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort cleanup only; preserve the original export failure.
        }
    }

    private static async Task<long> ExportTableAsync(
        SqlConnection sqlServer,
        SqliteConnection sqlite,
        TableMetadata table,
        int batchSize,
        int? commandTimeout,
        IProgress<BridgeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var columns = table.ExportedColumns;
        var selectColumns = string.Join(", ", columns.Select(c => BridgeIdentifier.QuoteSqlServerName(c.Name)));
        await using var select = sqlServer.CreateCommand();
        select.CommandText = $"SELECT {selectColumns} FROM {BridgeIdentifier.QuoteSqlServerTable(table.Name)}{SqlServerSchemaReader.BuildWhereSql(table.WhereClauses)}";
        if (commandTimeout.HasValue)
        {
            select.CommandTimeout = commandTimeout.Value;
        }

        await using var reader = await select.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        var insertColumnNames = string.Join(", ", columns.Select(c => BridgeIdentifier.QuoteSqliteName(c.Name)));
        var parameterNames = columns.Select((_, index) => "$p" + index).ToArray();
        var insertSql = $"INSERT INTO {BridgeIdentifier.QuoteSqliteName(table.SqliteTableName)} ({insertColumnNames}) VALUES ({string.Join(", ", parameterNames)})";

        long rowCount = 0;
        SqliteTransaction? transaction = null;
        SqliteCommand? insert = null;
        try
        {
            transaction = (SqliteTransaction)await sqlite.BeginTransactionAsync(cancellationToken);
            insert = sqlite.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = insertSql;
            for (var i = 0; i < parameterNames.Length; i++)
            {
                insert.Parameters.Add(parameterNames[i], SqliteTypeFor(columns[i]));
            }

            while (await reader.ReadAsync(cancellationToken))
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    var value = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                    ValueConverter.BindSqliteParameter(insert.Parameters[i], ValueConverter.ToSqliteValue(value, columns[i]));
                }

                await insert.ExecuteNonQueryAsync(cancellationToken);
                rowCount++;

                if (rowCount % batchSize == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    progress?.Report(new BridgeProgress(BridgeProgressKind.RowsCopied, table.Name.FullName, rowCount, table.EstimatedSourceRowCount));
                    await transaction.DisposeAsync();
                    transaction = (SqliteTransaction)await sqlite.BeginTransactionAsync(cancellationToken);
                    insert.Transaction = transaction;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            if (rowCount == 0 || rowCount % batchSize != 0)
            {
                progress?.Report(new BridgeProgress(BridgeProgressKind.RowsCopied, table.Name.FullName, rowCount, table.EstimatedSourceRowCount));
            }

            return rowCount;
        }
        finally
        {
            if (insert is not null)
            {
                await insert.DisposeAsync();
            }

            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static SqliteType SqliteTypeFor(ColumnMetadata column)
    {
        return ValueConverter.SqliteTypeFor(column) switch
        {
            "INTEGER" => SqliteType.Integer,
            "REAL" => SqliteType.Real,
            "BLOB" => SqliteType.Blob,
            _ => SqliteType.Text
        };
    }

    private static IReadOnlyList<string> BuildExportWarnings(ExportPlan plan, ExportOptions options)
    {
        var warnings = new List<string>(plan.Warnings);
        foreach (var table in plan.Tables)
        {
            var batchSize = BatchPlanner.GetEffectiveBatchSize(options, table.EstimatedSourceRowCount, table.EstimatedSourceBytes);
            if (options.AdaptiveBatchingEnabled && batchSize < options.BatchSize)
            {
                warnings.Add($"Adaptive batching set export batch size for '{table.Name.FullName}' to {batchSize} rows.");
            }
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ReportWarnings(IProgress<BridgeProgress>? progress, IReadOnlyList<string> warnings)
    {
        if (progress is null)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            progress.Report(new BridgeProgress(BridgeProgressKind.Warning, Message: warning));
        }
    }
}
