namespace Zachtbeer.SqlDataBridge.Models;

/// <summary>
/// Describes the kind of progress reported by an export or import operation.
/// </summary>
public enum BridgeProgressKind
{
    /// <summary>
    /// The operation has started.
    /// </summary>
    OperationStarted = 0,

    /// <summary>
    /// A table transfer has started.
    /// </summary>
    TableStarted = 1,

    /// <summary>
    /// Rows have been copied for the current table.
    /// </summary>
    RowsCopied = 2,

    /// <summary>
    /// A table transfer has completed.
    /// </summary>
    TableCompleted = 3,

    /// <summary>
    /// A non-fatal warning was produced.
    /// </summary>
    Warning = 4,

    /// <summary>
    /// The operation has completed.
    /// </summary>
    OperationCompleted = 5
}

/// <summary>
/// Reports progress from an export or import operation.
/// </summary>
/// <param name="Kind">The progress event kind.</param>
/// <param name="TableName">The source table full name when the event is table-specific.</param>
/// <param name="RowsProcessed">The rows processed for the table or operation.</param>
/// <param name="TotalRows">The expected rows when known.</param>
/// <param name="Message">An optional human-readable message.</param>
public sealed record BridgeProgress(BridgeProgressKind Kind, string? TableName = null, long RowsProcessed = 0, long? TotalRows = null, string? Message = null);

/// <summary>
/// Describes a column stored in a SQLite bridge package.
/// </summary>
public sealed record BridgeColumnManifest(string Name, int Ordinal, string SqlServerTypeName, short MaxLength, byte Precision, byte Scale, bool IsNullable, bool IsIdentity, bool IsComputed, bool IsExcluded, string? CollationName);

/// <summary>
/// Describes a table stored in or planned for a SQLite bridge package.
/// </summary>
public sealed record BridgeTableManifest(string SourceSchema, string SourceTable, string SqliteTable, long ExportedRowCount, long EstimatedSourceRowCount, long EstimatedSourceBytes, int ExportBatchSize, IReadOnlyList<BridgeColumnManifest> Columns)
{
    /// <summary>
    /// Gets the source SQL Server table full name.
    /// </summary>
    public string FullName => $"{SourceSchema}.{SourceTable}";
}

/// <summary>
/// Describes a SQLite bridge package or planned package.
/// </summary>
public sealed record BridgePackageManifest(int PackageFormatVersion, string ApplicationVersion, DateTimeOffset ExportedAtUtc, string SourceSchemaHash, IReadOnlyList<BridgeTableManifest> Tables, IReadOnlyList<string> ImportOrder, IReadOnlyList<string> Exclusions, IReadOnlyList<string> Warnings, bool ContainsDacpac, DacpacSchemaScope? DacpacSchemaScope, int? SourceEngineEdition = null);

/// <summary>
/// Summarizes validation performed before an export or import copies rows.
/// </summary>
public sealed record BridgePreflightResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, BridgePackageManifest? Manifest);
