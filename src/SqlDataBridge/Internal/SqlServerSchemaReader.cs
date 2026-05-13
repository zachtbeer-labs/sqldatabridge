using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Zachtbeer.SqlDataBridge.Models;

namespace Zachtbeer.SqlDataBridge.Internal;

internal static class SqlServerSchemaReader
{
    public static async Task<ExportPlan> CreateExportPlanAsync(
        string connectionString,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        BridgeIdentifier.NormalizeSqliteDataTablePrefix(options.DataTablePrefix);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var allTables = await ReadTablesAsync(connection, options.CommandTimeout, cancellationToken);
        var selected = ResolveTables(allTables, options);
        ValidateColumnExclusions(selected, options);
        ValidateGlobalWhereClauses(options);
        ValidatePerTableWhereClauses(selected, options);
        var warnings = new List<string>();
        var tableStats = await ReadTableStatsAsync(connection, options.CommandTimeout, warnings, cancellationToken);

        var excludedColumnSet = options.ExcludeColumns
            .Select(BridgeIdentifier.ParseColumnPath)
            .Select(c => $"{c.Schema}.{c.Table}.{c.Column}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tables = new List<TableMetadata>();
        foreach (var table in selected)
        {
            var columns = await ReadColumnsAsync(connection, table, excludedColumnSet, options.CommandTimeout, cancellationToken);
            var whereClauses = ResolveWhereClauses(table, columns, options);
            tableStats.TryGetValue(table.FullName, out var stats);
            var estimatedRows = whereClauses.Count == 0
                ? stats?.EstimatedSourceRowCount ?? 0
                : await CountFilteredRowsAsync(connection, table, whereClauses, options.CommandTimeout, cancellationToken);
            tables.Add(new TableMetadata(
                table,
                BridgeIdentifier.ToSqliteDataTableName(table, options.DataTablePrefix),
                columns,
                estimatedRows,
                stats?.EstimatedSourceBytes ?? 0,
                AppliedWhereClauses: whereClauses));
        }

        ValidateGlobalWhereClauseMatches(tables, options.GlobalWhereClauses);
        ValidateSupported(tables);

        var allForeignKeys = await ReadForeignKeysAsync(connection, options.CommandTimeout, cancellationToken);
        var selectedNames = selected.Select(t => t.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = allForeignKeys
            .Where(fk => selectedNames.Contains(fk.ParentTable.FullName) && selectedNames.Contains(fk.ReferencedTable.FullName))
            .ToArray();
        warnings.AddRange(BuildForeignKeyScopeWarnings(allForeignKeys, selectedNames));
        var importOrder = ImportPlanner.BuildImportOrder(selected, foreignKeys);
        var schemaHash = ComputeSchemaHash(tables);
        var skippedTables = allTables
            .Where(t => !selected.Contains(t))
            .Select(t => t.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var skippedColumns = tables
            .SelectMany(t => t.Columns.Where(c => c.IsComputed || c.IsExcluded))
            .Select(c => $"{c.Table.FullName}.{c.Name}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExportPlan(
            tables.OrderBy(t => t.Name.FullName, StringComparer.OrdinalIgnoreCase).ToArray(),
            foreignKeys,
            importOrder,
            warnings,
            skippedTables,
            skippedColumns,
            schemaHash);
    }

    public static async Task ValidateImportTargetAsync(
        string connectionString,
        IReadOnlyList<TableMetadata> tables,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var table in tables)
        {
            if (!await TableExistsAsync(connection, table.Name, commandTimeout, cancellationToken))
            {
                throw new BridgeException($"Target table '{table.Name.FullName}' does not exist. Create the target schema before import or exclude this table from the export scope.");
            }

            var targetColumns = await ReadTargetColumnsAsync(connection, table.Name, commandTimeout, cancellationToken);
            foreach (var column in table.ExportedColumns)
            {
                if (!targetColumns.TryGetValue(column.Name, out _))
                {
                    throw new BridgeException($"Target column '{table.Name.FullName}.{column.Name}' does not exist. Create the target column before import or exclude the source column during export.");
                }
            }

            foreach (var target in targetColumns.Values)
            {
                if (table.ExportedColumns.Any(c => string.Equals(c.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                    || target.IsComputed
                    || target.IsIdentity)
                {
                    continue;
                }

                if (!target.IsNullable && !target.HasDefault)
                {
                    throw new BridgeException($"Extra target column '{table.Name.FullName}.{target.Name}' is not nullable or defaulted. Make the column nullable, add a default constraint, or remove the table from the import scope.");
                }
            }

            var count = await ScalarLongAsync(connection, $"SELECT COUNT_BIG(*) FROM {BridgeIdentifier.QuoteSqlServerTable(table.Name)}", commandTimeout, cancellationToken);
            if (count != 0)
            {
                throw new BridgeException($"Target table '{table.Name.FullName}' must be empty before import. Empty the target table and retry.");
            }
        }
    }

    private static async Task<IReadOnlyList<TableName>> ReadTablesAsync(SqlConnection connection, int? commandTimeout, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.name, t.name
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var result = new List<TableName>();
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TableName(reader.GetString(0), reader.GetString(1)));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, TableSizeEstimate>> ReadTableStatsAsync(
        SqlConnection connection,
        int? commandTimeout,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.name,
                t.name,
                COALESCE(SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.row_count ELSE 0 END), 0) AS estimated_rows,
                COALESCE(SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.used_page_count ELSE 0 END), 0) * 8192 AS estimated_bytes
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.dm_db_partition_stats ps ON ps.object_id = t.object_id
            WHERE t.is_ms_shipped = 0
            GROUP BY s.name, t.name;
            """;

        var result = new Dictionary<string, TableSizeEstimate>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var command = new SqlCommand(sql, connection);
            ApplyTimeout(command, commandTimeout);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = new TableName(reader.GetString(0), reader.GetString(1));
                result[table.FullName] = new TableSizeEstimate(reader.GetInt64(2), reader.GetInt64(3));
            }
        }
        catch (SqlException exception)
        {
            warnings.Add($"Could not read SQL Server table size metadata from sys.dm_db_partition_stats. Adaptive batching will use caller batch sizes for unknown-size tables. SQL Server reported: {exception.Message}");
        }

        return result;
    }

    private static IReadOnlyList<TableName> ResolveTables(IReadOnlyList<TableName> allTables, ExportOptions options)
    {
        foreach (var pattern in options.Tables)
        {
            if (!allTables.Any(t => BridgeIdentifier.MatchesPattern(t, pattern)))
            {
                throw new BridgeException($"Table pattern '{pattern}' did not match any user table.");
            }
        }

        var selected = options.TableSelection switch
        {
            ExportTableSelectionMode.AllExcept => allTables
                .Where(t => !options.Tables.Any(p => BridgeIdentifier.MatchesPattern(t, p))),
            ExportTableSelectionMode.Only when options.Tables.Count == 0 =>
                throw new BridgeException("TableSelection Only requires at least one table pattern."),
            ExportTableSelectionMode.Only => allTables
                .Where(t => options.Tables.Any(p => BridgeIdentifier.MatchesPattern(t, p))),
            _ => throw new BridgeException($"TableSelection '{options.TableSelection}' is not supported.")
        };

        var result = selected
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (result.Count == 0)
        {
            throw new BridgeException("No tables are selected for export.");
        }

        return result;
    }

    private static void ValidateColumnExclusions(IReadOnlyList<TableName> selected, ExportOptions options)
    {
        foreach (var exclusion in options.ExcludeColumns)
        {
            var parsed = BridgeIdentifier.ParseColumnPath(exclusion);
            if (!selected.Any(t =>
                    string.Equals(t.Schema, parsed.Schema, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Name, parsed.Table, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BridgeException($"Column exclusion '{exclusion}' references a table outside the selected export scope.");
            }
        }
    }

    private static void ValidateGlobalWhereClauses(ExportOptions options)
    {
        foreach (var clause in options.GlobalWhereClauses)
        {
            if (string.IsNullOrWhiteSpace(clause.ColumnName))
            {
                throw new BridgeException("Global WHERE clause column name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(clause.WhereClause))
            {
                throw new BridgeException($"Global WHERE clause for column '{clause.ColumnName}' cannot be empty.");
            }
        }
    }

    private static void ValidatePerTableWhereClauses(IReadOnlyList<TableName> selected, ExportOptions options)
    {
        foreach (var clause in options.PerTableWhereClauses)
        {
            if (string.IsNullOrWhiteSpace(clause.TableName))
            {
                throw new BridgeException("Per-table WHERE clause table name cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(clause.WhereClause))
            {
                throw new BridgeException($"Per-table WHERE clause for table '{clause.TableName}' cannot be empty.");
            }

            if (!selected.Any(t => string.Equals(t.FullName, clause.TableName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new BridgeException($"Per-table WHERE clause table '{clause.TableName}' is not in the selected export scope.");
            }
        }
    }

    private static IReadOnlyList<string> ResolveWhereClauses(
        TableName table,
        IReadOnlyList<ColumnMetadata> columns,
        ExportOptions options)
    {
        return ResolveGlobalWhereClauses(columns, options.GlobalWhereClauses)
            .Concat(ResolvePerTableWhereClauses(table, options.PerTableWhereClauses))
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveGlobalWhereClauses(
        IReadOnlyList<ColumnMetadata> columns,
        IReadOnlyCollection<GlobalWhereClause> globalWhereClauses)
    {
        return globalWhereClauses
            .Where(clause => columns.Any(c => string.Equals(c.Name, clause.ColumnName, StringComparison.OrdinalIgnoreCase)))
            .Select(clause => clause.WhereClause.Trim())
            .ToArray();
    }

    private static IReadOnlyList<string> ResolvePerTableWhereClauses(
        TableName table,
        IReadOnlyCollection<PerTableWhereClause> perTableWhereClauses)
    {
        return perTableWhereClauses
            .Where(clause => string.Equals(table.FullName, clause.TableName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(clause => clause.WhereClause.Trim())
            .ToArray();
    }

    private static void ValidateGlobalWhereClauseMatches(
        IReadOnlyList<TableMetadata> tables,
        IReadOnlyCollection<GlobalWhereClause> globalWhereClauses)
    {
        foreach (var clause in globalWhereClauses)
        {
            if (!tables.Any(t => t.Columns.Any(c => string.Equals(c.Name, clause.ColumnName, StringComparison.OrdinalIgnoreCase))))
            {
                throw new BridgeException($"Global WHERE clause column '{clause.ColumnName}' did not match any selected source table.");
            }
        }
    }

    private static async Task<long> CountFilteredRowsAsync(
        SqlConnection connection,
        TableName table,
        IReadOnlyList<string> whereClauses,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        var whereSql = BuildWhereSql(whereClauses);
        return await ScalarLongAsync(connection, $"SELECT COUNT_BIG(*) FROM {BridgeIdentifier.QuoteSqlServerTable(table)}{whereSql}", commandTimeout, cancellationToken);
    }

    internal static string BuildWhereSql(IReadOnlyList<string> whereClauses)
    {
        if (whereClauses.Count == 0)
        {
            return string.Empty;
        }

        return " WHERE " + string.Join(" AND ", whereClauses.Select(clause => $"({clause})"));
    }

    private static async Task<IReadOnlyList<ColumnMetadata>> ReadColumnsAsync(
        SqlConnection connection,
        TableName table,
        ISet<string> excludedColumns,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c.name,
                c.column_id,
                ty.name,
                c.max_length,
                c.precision,
                c.scale,
                c.is_nullable,
                c.is_identity,
                c.is_computed,
                c.collation_name
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY c.column_id;
            """;

        var result = new List<ColumnMetadata>();
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        command.Parameters.AddWithValue("@schema", table.Schema);
        command.Parameters.AddWithValue("@table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            result.Add(new ColumnMetadata(
                table,
                name,
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt16(3),
                reader.GetByte(4),
                reader.GetByte(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                excludedColumns.Contains($"{table.Schema}.{table.Name}.{name}")));
        }

        foreach (var excluded in excludedColumns.Where(c => c.StartsWith(table.FullName + ".", StringComparison.OrdinalIgnoreCase)))
        {
            var columnName = excluded[(table.FullName.Length + 1)..];
            if (!result.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BridgeException($"Excluded column '{excluded}' does not exist.");
            }
        }

        return result;
    }

    private static void ValidateSupported(IEnumerable<TableMetadata> tables)
    {
        foreach (var column in tables.SelectMany(t => t.Columns).Where(c => c.IsExported))
        {
            if (ValueConverter.IsUnsupported(column.SqlServerTypeName))
            {
                throw new BridgeException($"Unsupported included type '{column.SqlServerTypeName}' on {column.Table.FullName}.{column.Name}. Exclude '{column.Table.FullName}.{column.Name}' explicitly or remove '{column.Table.FullName}' from the export scope.");
            }

            _ = ValueConverter.SqliteTypeFor(column);
        }
    }

    private static async Task<IReadOnlyList<ForeignKeyMetadata>> ReadForeignKeysAsync(
        SqlConnection connection,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.name AS parent_schema,
                pt.name AS parent_table,
                rs.name AS referenced_schema,
                rt.name AS referenced_table
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id;
            """;

        var result = new List<ForeignKeyMetadata>();
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var parent = new TableName(reader.GetString(0), reader.GetString(1));
            var referenced = new TableName(reader.GetString(2), reader.GetString(3));
            result.Add(new ForeignKeyMetadata(parent, referenced));
        }

        return result;
    }

    private static IReadOnlyList<string> BuildForeignKeyScopeWarnings(
        IReadOnlyList<ForeignKeyMetadata> foreignKeys,
        ISet<string> selectedTables)
    {
        return foreignKeys
            .Where(fk => selectedTables.Contains(fk.ParentTable.FullName)
                && !selectedTables.Contains(fk.ReferencedTable.FullName))
            .Select(fk => $"Selected table '{fk.ParentTable.FullName}' has a foreign key to unselected table '{fk.ReferencedTable.FullName}'. Import into an empty target can fail unless the referenced table is prepared separately.")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ComputeSchemaHash(IReadOnlyList<TableMetadata> tables)
    {
        var builder = new StringBuilder();
        foreach (var table in tables.OrderBy(t => t.Name.FullName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(table.Name.FullName);
            foreach (var column in table.Columns.OrderBy(c => c.Ordinal))
            {
                builder.Append(column.Name).Append('|')
                    .Append(column.Ordinal).Append('|')
                    .Append(column.SqlServerTypeName).Append('|')
                    .Append(column.MaxLength).Append('|')
                    .Append(column.Precision).Append('|')
                    .Append(column.Scale).Append('|')
                    .Append(column.IsNullable).Append('|')
                    .Append(column.IsIdentity).Append('|')
                    .Append(column.IsComputed).Append('|')
                    .Append(column.CollationName)
                    .AppendLine();
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, TableName table, int? commandTimeout, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(*)
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table;
        """;
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        command.Parameters.AddWithValue("@schema", table.Schema);
        command.Parameters.AddWithValue("@table", table.Name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private sealed record TargetColumn(string Name, bool IsNullable, bool IsIdentity, bool IsComputed, bool HasDefault);

    private sealed record TableSizeEstimate(long EstimatedSourceRowCount, long EstimatedSourceBytes);

    private static async Task<IReadOnlyDictionary<string, TargetColumn>> ReadTargetColumnsAsync(
        SqlConnection connection,
        TableName table,
        int? commandTimeout,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.name, c.is_nullable, c.is_identity, c.is_computed, CASE WHEN dc.object_id IS NULL THEN 0 ELSE 1 END
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE s.name = @schema AND t.name = @table;
            """;

        var result = new Dictionary<string, TargetColumn>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        command.Parameters.AddWithValue("@schema", table.Schema);
        command.Parameters.AddWithValue("@table", table.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var column = new TargetColumn(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetInt32(4) == 1);
            result[column.Name] = column;
        }

        return result;
    }

    private static async Task<long> ScalarLongAsync(SqlConnection connection, string sql, int? commandTimeout, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        ApplyTimeout(command, commandTimeout);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void ApplyTimeout(SqlCommand command, int? commandTimeout)
    {
        if (commandTimeout.HasValue)
        {
            command.CommandTimeout = commandTimeout.Value;
        }
    }
}
