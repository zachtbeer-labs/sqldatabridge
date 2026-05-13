using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Models;

/// <summary>
/// Controls whether SQL Server schema is captured during export.
/// </summary>
public enum SchemaCaptureMode
{
    /// <summary>
    /// Do not capture SQL Server schema. This is the default data-only behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Extract the source database schema as a dacpac and store it in the SQLite package.
    /// </summary>
    Dacpac = 1
}

/// <summary>
/// Controls whether package schema is deployed before import.
/// </summary>
public enum SchemaDeploymentMode
{
    /// <summary>
    /// Do not deploy schema. This is the default behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Deploy the dacpac stored in the SQLite package before importing data.
    /// </summary>
    DeployDacpac = 1
}

/// <summary>
/// Controls which schema objects are stored in a dacpac captured during export.
/// </summary>
public enum DacpacSchemaScope
{
    /// <summary>
    /// Capture the source database schema model.
    /// </summary>
    Database = 0,

    /// <summary>
    /// Capture only tables selected by the export plan and required scriptable dependencies.
    /// </summary>
    SelectedExportTables = 1
}

/// <summary>
/// Controls how export table patterns are interpreted.
/// </summary>
public enum ExportTableSelectionMode
{
    /// <summary>
    /// Export all user tables except those matching <see cref="ExportOptions.Tables"/>.
    /// </summary>
    AllExcept = 0,

    /// <summary>
    /// Export only user tables matching <see cref="ExportOptions.Tables"/>.
    /// </summary>
    Only = 1
}

/// <summary>
/// Configures dacpac schema capture during export.
/// </summary>
public sealed class DacpacCaptureOptions
{
    /// <summary>
    /// Gets the schema scope stored in the captured dacpac.
    /// </summary>
    public DacpacSchemaScope SchemaScope { get; init; } = DacpacSchemaScope.Database;

    /// <summary>
    /// Gets whether extraction should include referenced server-scoped elements, such as logins.
    /// </summary>
    public bool ExtractReferencedServerScopedElements { get; init; }

    /// <summary>
    /// Gets whether extraction should include only objects scoped to the source application.
    /// </summary>
    public bool ExtractApplicationScopedObjectsOnly { get; init; }

    /// <summary>
    /// Gets whether permissions should be ignored while extracting the dacpac.
    /// </summary>
    public bool IgnorePermissions { get; init; } = true;

    /// <summary>
    /// Gets whether user-to-login mappings should be ignored while extracting the dacpac.
    /// </summary>
    public bool IgnoreUserLoginMappings { get; init; } = true;

    /// <summary>
    /// Gets whether DacFx should verify the extracted model.
    /// </summary>
    public bool VerifyExtraction { get; init; } = true;
}

/// <summary>
/// Configures dacpac schema deployment during import.
/// </summary>
public sealed class DacpacDeploymentOptions
{
    /// <summary>
    /// Gets whether DacFx should allow deployment when the source and target platforms are incompatible.
    /// </summary>
    public bool AllowIncompatiblePlatform { get; init; }

    /// <summary>
    /// Gets whether deployment should block when DacFx detects possible data loss.
    /// </summary>
    public bool BlockOnPossibleDataLoss { get; init; } = true;

    /// <summary>
    /// Gets whether target objects absent from the package schema may be dropped.
    /// </summary>
    public bool AllowObjectDrops { get; init; }

    /// <summary>
    /// Gets whether database users should be deployed from the dacpac.
    /// </summary>
    public bool DeployUsers { get; init; }

    /// <summary>
    /// Gets whether server logins and login mappings should be deployed from the dacpac when present.
    /// </summary>
    public bool DeployLogins { get; init; }

    /// <summary>
    /// Gets whether permissions should be deployed from the dacpac.
    /// </summary>
    public bool DeployPermissions { get; init; }

    /// <summary>
    /// Gets whether role membership should be deployed from the dacpac.
    /// </summary>
    public bool DeployRoleMembership { get; init; }

    /// <summary>
    /// Gets whether database files and filegroups should be deployed from the dacpac.
    /// </summary>
    public bool DeployDatabaseFiles { get; init; }

    /// <summary>
    /// Gets whether DacFx should verify the deployment plan before applying changes.
    /// </summary>
    public bool VerifyDeployment { get; init; } = true;
}

/// <summary>
/// Configures a SQL Server WHERE predicate that applies to selected export tables containing a column.
/// </summary>
/// <param name="ColumnName">The source column name that gates this predicate.</param>
/// <param name="WhereClause">The raw SQL Server predicate to append to matching table exports.</param>
public sealed record GlobalWhereClause(string ColumnName, string WhereClause);

/// <summary>
/// Configures a SQL Server WHERE predicate that applies to one selected export table.
/// </summary>
/// <param name="TableName">The source table name formatted as <c>&lt;schema&gt;.&lt;table&gt;</c>.</param>
/// <param name="WhereClause">The raw SQL Server predicate to append to the table export.</param>
public sealed record PerTableWhereClause(string TableName, string WhereClause);

/// <summary>
/// Configures a SQL Server to SQLite export.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>
    /// Gets how <see cref="Tables"/> is applied during export.
    /// </summary>
    public ExportTableSelectionMode TableSelection { get; init; } = ExportTableSelectionMode.AllExcept;

    /// <summary>
    /// Gets table patterns used by <see cref="TableSelection"/>. Patterns support exact source table names and <c>*</c> wildcards.
    /// </summary>
    public IReadOnlyCollection<string> Tables { get; init; } = [];

    /// <summary>
    /// Gets the column paths to omit from the package, formatted as <c>&lt;schema&gt;.&lt;table&gt;.&lt;column&gt;</c>.
    /// </summary>
    public IReadOnlyCollection<string> ExcludeColumns { get; init; } = [];

    /// <summary>
    /// Gets global SQL Server WHERE predicates applied to selected tables containing the configured source column.
    /// </summary>
    public IReadOnlyCollection<GlobalWhereClause> GlobalWhereClauses { get; init; } = [];

    /// <summary>
    /// Gets SQL Server WHERE predicates applied to exact selected source tables.
    /// </summary>
    public IReadOnlyCollection<PerTableWhereClause> PerTableWhereClauses { get; init; } = [];

    /// <summary>
    /// Gets the SQLite table-name prefix used for exported data tables. Use <see langword="null"/> or an empty value to omit the prefix.
    /// </summary>
    public string? DataTablePrefix { get; init; } = "zsb_data";

    /// <summary>
    /// Gets the number of rows to write per batch.
    /// </summary>
    public int BatchSize { get; init; } = 1_000;

    /// <summary>
    /// Gets whether table size metadata should reduce batch sizes for large or wide tables.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; init; } = true;

    /// <summary>
    /// Gets the estimated table size, in bytes, at which large-table batching is used.
    /// </summary>
    public long LargeTableThresholdBytes { get; init; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Gets the estimated table row count at which large-table batching is used when size metadata is unavailable.
    /// </summary>
    public long LargeTableRowThreshold { get; init; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Gets the row batch size used for tables at or above the configured large-table thresholds.
    /// </summary>
    public int LargeTableBatchSize { get; init; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Gets the approximate maximum bytes represented by one batch when table size and row count metadata are available.
    /// </summary>
    public long MaxBatchBytes { get; init; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server metadata and data commands during export.
    /// </summary>
    public int? CommandTimeout { get; init; }

    /// <summary>
    /// Gets the optional progress reporter for export operations.
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; init; }

    /// <summary>
    /// Gets whether an existing SQLite package at the destination path may be replaced after a successful export.
    /// </summary>
    public bool OverwriteExistingPackage { get; init; }

    /// <summary>
    /// Gets whether SQL Server schema should be captured in the SQLite package.
    /// </summary>
    public SchemaCaptureMode SchemaCaptureMode { get; init; } = SchemaCaptureMode.None;

    /// <summary>
    /// Gets dacpac extraction settings used when <see cref="SchemaCaptureMode"/> is <see cref="SchemaCaptureMode.Dacpac"/>.
    /// </summary>
    public DacpacCaptureOptions DacpacCaptureOptions { get; init; } = new();
}

/// <summary>
/// Configures a SQLite package import into SQL Server.
/// </summary>
public sealed class ImportOptions
{
    /// <summary>
    /// Gets the number of rows to import per bulk-copy batch.
    /// </summary>
    public int BatchSize { get; init; } = 1_000;

    /// <summary>
    /// Gets whether package table size metadata should reduce bulk-copy batch sizes for large or wide tables.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; init; } = true;

    /// <summary>
    /// Gets the estimated table size, in bytes, at which large-table batching is used.
    /// </summary>
    public long LargeTableThresholdBytes { get; init; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Gets the estimated table row count at which large-table batching is used when size metadata is unavailable.
    /// </summary>
    public long LargeTableRowThreshold { get; init; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Gets the row batch size used for tables at or above the configured large-table thresholds.
    /// </summary>
    public int LargeTableBatchSize { get; init; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Gets the approximate maximum bytes represented by one bulk-copy batch when table size and row count metadata are available.
    /// </summary>
    public long MaxBatchBytes { get; init; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server target validation commands during import.
    /// </summary>
    public int? ValidationCommandTimeout { get; init; }

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server bulk-copy operations during import.
    /// </summary>
    public int? BulkCopyTimeout { get; init; }

    /// <summary>
    /// Gets the optional progress reporter for import operations.
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; init; }

    /// <summary>
    /// Gets whether schema stored in the package should be deployed before data import.
    /// </summary>
    public SchemaDeploymentMode SchemaDeploymentMode { get; init; } = SchemaDeploymentMode.None;

    /// <summary>
    /// Gets dacpac deployment settings used when <see cref="SchemaDeploymentMode"/> is <see cref="SchemaDeploymentMode.DeployDacpac"/>.
    /// </summary>
    public DacpacDeploymentOptions DacpacDeploymentOptions { get; init; } = new();
}

/// <summary>
/// Shared compatibility options for callers that want to pass one options object to export and import.
/// Prefer <see cref="ExportOptions"/> and <see cref="ImportOptions"/> for new code.
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>
    /// Gets how <see cref="Tables"/> is applied during export.
    /// </summary>
    public ExportTableSelectionMode TableSelection { get; init; } = ExportTableSelectionMode.AllExcept;

    /// <summary>
    /// Gets table patterns used by <see cref="TableSelection"/> during export. Patterns support exact source table names and <c>*</c> wildcards.
    /// </summary>
    public IReadOnlyCollection<string> Tables { get; init; } = [];

    /// <summary>
    /// Gets the column paths to omit during export.
    /// </summary>
    public IReadOnlyCollection<string> ExcludeColumns { get; init; } = [];

    /// <summary>
    /// Gets global SQL Server WHERE predicates applied to selected export tables containing the configured source column.
    /// </summary>
    public IReadOnlyCollection<GlobalWhereClause> GlobalWhereClauses { get; init; } = [];

    /// <summary>
    /// Gets SQL Server WHERE predicates applied to exact selected source tables during export.
    /// </summary>
    public IReadOnlyCollection<PerTableWhereClause> PerTableWhereClauses { get; init; } = [];

    /// <summary>
    /// Gets the SQLite table-name prefix used for exported data tables. Use <see langword="null"/> or an empty value to omit the prefix.
    /// </summary>
    public string? DataTablePrefix { get; init; } = "zsb_data";

    /// <summary>
    /// Gets the row batch size used by export and import.
    /// </summary>
    public int BatchSize { get; init; } = 1_000;

    /// <summary>
    /// Gets whether table size metadata should reduce batch sizes for large or wide tables.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; init; } = true;

    /// <summary>
    /// Gets the estimated table size, in bytes, at which large-table batching is used.
    /// </summary>
    public long LargeTableThresholdBytes { get; init; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Gets the estimated table row count at which large-table batching is used when size metadata is unavailable.
    /// </summary>
    public long LargeTableRowThreshold { get; init; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Gets the row batch size used for tables at or above the configured large-table thresholds.
    /// </summary>
    public int LargeTableBatchSize { get; init; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Gets the approximate maximum bytes represented by one batch when table size and row count metadata are available.
    /// </summary>
    public long MaxBatchBytes { get; init; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server metadata and data commands during export.
    /// </summary>
    public int? ExportCommandTimeout { get; init; }

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server target validation commands during import.
    /// </summary>
    public int? ImportValidationCommandTimeout { get; init; }

    /// <summary>
    /// Gets the optional timeout, in seconds, for SQL Server bulk-copy operations during import.
    /// </summary>
    public int? ImportBulkCopyTimeout { get; init; }

    /// <summary>
    /// Gets the optional progress reporter used by export and import operations.
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; init; }

    /// <summary>
    /// Gets whether an existing SQLite package at the destination path may be replaced after a successful export.
    /// </summary>
    public bool OverwriteExistingPackage { get; init; }

    /// <summary>
    /// Gets whether SQL Server schema should be captured in the SQLite package during export.
    /// </summary>
    public SchemaCaptureMode SchemaCaptureMode { get; init; } = SchemaCaptureMode.None;

    /// <summary>
    /// Gets dacpac extraction settings used when schema capture is enabled.
    /// </summary>
    public DacpacCaptureOptions DacpacCaptureOptions { get; init; } = new();

    /// <summary>
    /// Gets whether schema stored in the package should be deployed before data import.
    /// </summary>
    public SchemaDeploymentMode SchemaDeploymentMode { get; init; } = SchemaDeploymentMode.None;

    /// <summary>
    /// Gets dacpac deployment settings used when schema deployment is enabled.
    /// </summary>
    public DacpacDeploymentOptions DacpacDeploymentOptions { get; init; } = new();

    internal ExportOptions ToExportOptions()
    {
        return new ExportOptions
        {
            TableSelection = TableSelection,
            Tables = Tables,
            ExcludeColumns = ExcludeColumns,
            GlobalWhereClauses = GlobalWhereClauses,
            PerTableWhereClauses = PerTableWhereClauses,
            DataTablePrefix = DataTablePrefix,
            BatchSize = BatchSize,
            AdaptiveBatchingEnabled = AdaptiveBatchingEnabled,
            LargeTableThresholdBytes = LargeTableThresholdBytes,
            LargeTableRowThreshold = LargeTableRowThreshold,
            LargeTableBatchSize = LargeTableBatchSize,
            MaxBatchBytes = MaxBatchBytes,
            CommandTimeout = ExportCommandTimeout,
            Progress = Progress,
            OverwriteExistingPackage = OverwriteExistingPackage,
            SchemaCaptureMode = SchemaCaptureMode,
            DacpacCaptureOptions = DacpacCaptureOptions
        };
    }

    internal ImportOptions ToImportOptions()
    {
        return new ImportOptions
        {
            BatchSize = BatchSize,
            AdaptiveBatchingEnabled = AdaptiveBatchingEnabled,
            LargeTableThresholdBytes = LargeTableThresholdBytes,
            LargeTableRowThreshold = LargeTableRowThreshold,
            LargeTableBatchSize = LargeTableBatchSize,
            MaxBatchBytes = MaxBatchBytes,
            ValidationCommandTimeout = ImportValidationCommandTimeout,
            BulkCopyTimeout = ImportBulkCopyTimeout,
            Progress = Progress,
            SchemaDeploymentMode = SchemaDeploymentMode,
            DacpacDeploymentOptions = DacpacDeploymentOptions
        };
    }
}

/// <summary>
/// Summarizes the tables, rows, and non-fatal warnings produced by an export or import operation.
/// </summary>
/// <param name="TableCount">The number of tables processed.</param>
/// <param name="RowCount">The number of rows processed across all tables.</param>
/// <param name="Warnings">The warning messages produced by the operation.</param>
public sealed record BridgeResult(int TableCount, long RowCount, IReadOnlyList<string> Warnings);

/// <summary>
/// Represents a validation or bridge operation error that callers can handle explicitly.
/// </summary>
public sealed class BridgeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public BridgeException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public BridgeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
