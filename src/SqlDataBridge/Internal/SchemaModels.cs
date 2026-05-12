namespace Zachtbeer.SqlDataBridge.Internal;

internal sealed record ColumnMetadata(
    TableName Table,
    string Name,
    int Ordinal,
    string SqlServerTypeName,
    short MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    string? CollationName,
    bool IsExcluded)
{
    public bool IsExported => !IsComputed && !IsExcluded;
}

internal sealed record TableMetadata(
    TableName Name,
    string SqliteTableName,
    IReadOnlyList<ColumnMetadata> Columns,
    long EstimatedSourceRowCount = 0,
    long EstimatedSourceBytes = 0,
    int ExportBatchSize = 0,
    IReadOnlyList<string>? AppliedWhereClauses = null)
{
    public IReadOnlyList<ColumnMetadata> ExportedColumns => Columns.Where(c => c.IsExported).OrderBy(c => c.Ordinal).ToArray();

    public IReadOnlyList<string> WhereClauses => AppliedWhereClauses ?? [];
}

internal sealed record ForeignKeyMetadata(TableName ParentTable, TableName ReferencedTable);

internal sealed record ExportPlan(
    IReadOnlyList<TableMetadata> Tables,
    IReadOnlyList<ForeignKeyMetadata> ForeignKeys,
    IReadOnlyList<TableName> ImportOrder,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> SkippedTables,
    IReadOnlyList<string> SkippedColumns,
    string SchemaHash);
