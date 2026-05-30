using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Models;

/// <summary>
/// Selects whether export captures SQL Server schema alongside data. Defaults to <see cref="None"/> (data-only).
/// </summary>
public enum SchemaCaptureMode
{
    /// <summary>
    /// Skips schema capture; the package contains data only. This is the default.
    /// </summary>
    None = 0,

    /// <summary>
    /// Extracts the source database schema as a dacpac and embeds it in the SQLite package, so import can recreate the schema.
    /// </summary>
    Dacpac = 1
}

/// <summary>
/// Selects whether import deploys the package's schema before loading data. Defaults to <see cref="None"/> (assume schema already exists).
/// </summary>
public enum SchemaDeploymentMode
{
    /// <summary>
    /// Skips schema deployment; the target database must already contain matching tables. This is the default.
    /// </summary>
    None = 0,

    /// <summary>
    /// Deploys the dacpac embedded in the SQLite package against the target before importing data.
    /// </summary>
    DeployDacpac = 1
}

/// <summary>
/// Selects which schema objects an export-time dacpac extraction includes. Defaults to <see cref="Database"/> (whole database model).
/// </summary>
public enum DacpacSchemaScope
{
    /// <summary>
    /// Captures the entire source database schema model. This is the default and matches DacFx's standard extract behavior.
    /// </summary>
    Database = 0,

    /// <summary>
    /// Captures only the tables chosen by the export plan plus the dependencies DacFx needs to script them — produces a smaller, plan-scoped dacpac.
    /// </summary>
    SelectedExportTables = 1
}

/// <summary>
/// Selects how <see cref="ExportOptions.Tables"/> patterns filter the export. Defaults to <see cref="AllExcept"/>.
/// </summary>
public enum ExportTableSelectionMode
{
    /// <summary>
    /// Exports every user table except those matching <see cref="ExportOptions.Tables"/> (i.e. patterns act as an exclusion list). This is the default.
    /// </summary>
    AllExcept = 0,

    /// <summary>
    /// Exports only the user tables matching <see cref="ExportOptions.Tables"/> (i.e. patterns act as an inclusion list).
    /// </summary>
    Only = 1
}

/// <summary>
/// Tunes how the dacpac is extracted when <see cref="ExportOptions.SchemaCaptureMode"/> is <see cref="SchemaCaptureMode.Dacpac"/>.
/// </summary>
public sealed class DacpacCaptureOptions
{
    /// <summary>
    /// Returns a new <see cref="DacpacCaptureOptions"/> populated with the documented defaults — a convenient starting point to tweak.
    /// Each access returns a fresh instance, so mutating the returned object never affects subsequent callers.
    /// </summary>
    public static DacpacCaptureOptions Default => new()
    {
        SchemaScope = DacpacSchemaScope.Database,
        ExtractReferencedServerScopedElements = false,
        ExtractApplicationScopedObjectsOnly = false,
        IgnorePermissions = true,
        IgnoreUserLoginMappings = true,
        VerifyExtraction = false
    };

    /// <summary>
    /// Controls which schema objects are extracted into the dacpac. Defaults to <see cref="DacpacSchemaScope.Database"/> (full database model).
    /// </summary>
    public DacpacSchemaScope SchemaScope { get; set; } = DacpacSchemaScope.Database;

    /// <summary>
    /// Includes server-scoped objects referenced by the database (e.g. logins) in the extracted dacpac. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ExtractReferencedServerScopedElements { get; set; }

    /// <summary>
    /// Restricts extraction to objects owned by the source application, skipping shared or system objects. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ExtractApplicationScopedObjectsOnly { get; set; }

    /// <summary>
    /// Strips GRANT/DENY/REVOKE statements from the extracted dacpac so the captured schema does not carry environment-specific permissions. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IgnorePermissions { get; set; } = true;

    /// <summary>
    /// Strips user-to-login mappings from the extracted dacpac so the captured schema is portable across servers. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IgnoreUserLoginMappings { get; set; } = true;

    /// <summary>
    /// Runs DacFx's post-extraction model verification. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Verification only <em>validates</em> the model DacFx has already built — it does not change which objects are extracted or the resulting dacpac bytes, and it is independent of the deploy-side <see cref="DacpacDeploymentOptions.VerifyDeployment"/> check. Leaving it off therefore captures exactly the same schema while avoiding <c>SQL71501</c> "unresolved reference" failures on procedures, views, and functions that work in the live source database but defeat DacFx's stricter static validator — ambiguous unqualified columns in multi-table joins, cross-database or three-part names, temp tables, and other deferred-resolvable references. These are common in real, legacy databases and create and run correctly on the target via SQL Server's own binding / deferred name resolution.
    /// </para>
    /// <para>
    /// Set to <see langword="true"/> to fail the export early when a captured object has a genuinely unresolvable reference (for example, a column that no longer exists on an existing table), rather than discovering it at deploy time. Note that verification has no per-rule suppression: enabling it re-enables <em>all</em> model-validation rules, so a single benign false positive will block the whole export.
    /// </para>
    /// </remarks>
    public bool VerifyExtraction { get; set; }
}

/// <summary>
/// Tunes how the package's embedded dacpac is deployed when <see cref="ImportOptions.SchemaDeploymentMode"/> is <see cref="SchemaDeploymentMode.DeployDacpac"/>.
/// </summary>
public sealed class DacpacDeploymentOptions
{
    /// <summary>
    /// Returns a new <see cref="DacpacDeploymentOptions"/> populated with the documented defaults — a convenient starting point to tweak.
    /// Each access returns a fresh instance, so mutating the returned object never affects subsequent callers.
    /// </summary>
    public static DacpacDeploymentOptions Default => new()
    {
        AllowIncompatiblePlatform = false,
        BlockOnPossibleDataLoss = true,
        AllowObjectDrops = false,
        DeployUsers = false,
        DeployLogins = false,
        DeployPermissions = false,
        DeployRoleMembership = false,
        DeployDatabaseFiles = false,
        DeployDatabaseOptions = false,
        AdaptAzureSourceForOnPremTarget = true,
        VerifyDeployment = true
    };

    /// <summary>
    /// Allows DacFx to deploy even when the source and target SQL platforms differ (e.g. on-prem dacpac to Azure SQL). Defaults to <see langword="false"/> (fail-fast on platform mismatch).
    /// </summary>
    public bool AllowIncompatiblePlatform { get; set; }

    /// <summary>
    /// Blocks deployment when DacFx detects an operation that could lose existing data (e.g. dropping a populated column). Defaults to <see langword="true"/>; set to <see langword="false"/> only for known-destructive migrations.
    /// </summary>
    public bool BlockOnPossibleDataLoss { get; set; } = true;

    /// <summary>
    /// Permits DacFx to drop target objects that are absent from the package schema. Defaults to <see langword="false"/> (extra target objects are preserved).
    /// </summary>
    public bool AllowObjectDrops { get; set; }

    /// <summary>
    /// Deploys database users from the dacpac. Defaults to <see langword="false"/> because users are usually environment-specific.
    /// </summary>
    public bool DeployUsers { get; set; }

    /// <summary>
    /// Deploys server logins and their user mappings from the dacpac, when present. Defaults to <see langword="false"/>; only useful when the captured dacpac actually carries login info.
    /// </summary>
    public bool DeployLogins { get; set; }

    /// <summary>
    /// Deploys GRANT/DENY/REVOKE statements from the dacpac. Defaults to <see langword="false"/>; turn on only when the source permissions should follow the schema.
    /// </summary>
    public bool DeployPermissions { get; set; }

    /// <summary>
    /// Deploys role membership assignments from the dacpac. Defaults to <see langword="false"/>.
    /// </summary>
    public bool DeployRoleMembership { get; set; }

    /// <summary>
    /// Deploys database file and filegroup definitions from the dacpac. Defaults to <see langword="false"/> because storage layout is usually managed per-environment.
    /// </summary>
    public bool DeployDatabaseFiles { get; set; }

    /// <summary>
    /// Applies the source database's <c>ALTER DATABASE</c> property scripts (containment, recovery model, compatibility-adjacent options, etc.) to the target. Defaults to <see langword="false"/> because these settings are usually environment-specific.
    /// When <see langword="false"/>, cross-platform model adaptation is delegated to <see cref="AdaptAzureSourceForOnPremTarget"/>; set this to <see langword="true"/> only when you genuinely want the source database options applied verbatim to the target.
    /// </summary>
    public bool DeployDatabaseOptions { get; set; }

    /// <summary>
    /// Rewrites Azure SQL-specific model elements on a temp copy of the dacpac when deploying an Azure-source extract to an on-prem (non-Azure) target, so DacFx no longer scripts prerequisites the target cannot satisfy. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="true"/>, the deploy probes <c>SERVERPROPERTY('EngineEdition')</c> on the target and — if the source package was stamped as Azure SQL (edition 5 / 8 / 11 / 12) and the target is non-Azure — performs the following model.xml mutations before invoking DacFx:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Removes the optional <c>Containment</c> property from <c>SqlDatabaseOptions</c>.</description></item>
    /// <item><description>Rewrites <c>SqlUser</c> elements whose <c>AuthenticationType</c> is 2 (contained password user) or 4 (Entra / external provider) to <c>IsWithoutLogin=True</c>, dropping the <c>Password</c> / <c>Sid</c> properties. The element is kept (not deleted) so model-internal references stay valid.</description></item>
    /// </list>
    /// <para>
    /// Without this rewrite, DacFx emits <c>ALTER DATABASE ... SET CONTAINMENT = PARTIAL</c> as a deploy-script prerequisite, which fails with Msg 12824 on targets where <c>sp_configure 'contained database authentication'</c> is 0.
    /// </para>
    /// <para>
    /// Set this to <see langword="false"/> to deploy the source model verbatim. Useful when you want the deploy to fail loudly so you can fix the source dacpac upstream, or when the operator has already configured <c>contained database authentication = 1</c> on the target and wants the original users preserved.
    /// </para>
    /// <para>
    /// The probe failing aborts the deploy with a <see cref="BridgeException"/>; disable this flag or <see cref="DeployDatabaseOptions"/> to bypass it. The source-platform signal travels in the SQLite package (column <c>source_engine_edition</c> on <c>zsb_schema_packages</c>); packages produced before that column existed are treated as "unknown source" and rewritten the same way a non-Azure target would be probed for, preserving pre-stamp behaviour.
    /// </para>
    /// </remarks>
    public bool AdaptAzureSourceForOnPremTarget { get; set; } = true;

    /// <summary>
    /// Runs DacFx's deployment-plan verification before applying changes; disable to skip the pre-flight check at the cost of catching issues only at apply time. Defaults to <see langword="true"/>.
    /// </summary>
    public bool VerifyDeployment { get; set; } = true;
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
    /// Returns a new <see cref="ExportOptions"/> populated with the documented defaults — a convenient starting point to tweak.
    /// Each access returns a fresh instance, so mutating the returned object never affects subsequent callers.
    /// </summary>
    public static ExportOptions Default => new()
    {
        TableSelection = ExportTableSelectionMode.AllExcept,
        DataTablePrefix = "zsb_data",
        BatchSize = 1_000,
        AdaptiveBatchingEnabled = true,
        LargeTableThresholdBytes = BatchPlanner.DefaultLargeTableThresholdBytes,
        LargeTableRowThreshold = BatchPlanner.DefaultLargeTableRowThreshold,
        LargeTableBatchSize = BatchPlanner.DefaultLargeTableBatchSize,
        MaxBatchBytes = BatchPlanner.DefaultMaxBatchBytes,
        OverwriteExistingPackage = false,
        SchemaCaptureMode = SchemaCaptureMode.None
    };

    /// <summary>
    /// Controls how <see cref="Tables"/> is interpreted: <see cref="ExportTableSelectionMode.AllExcept"/> uses it as an exclusion list against a full export, <see cref="ExportTableSelectionMode.Only"/> uses it as an inclusion list. Defaults to <see cref="ExportTableSelectionMode.AllExcept"/>.
    /// </summary>
    public ExportTableSelectionMode TableSelection { get; set; } = ExportTableSelectionMode.AllExcept;

    /// <summary>
    /// Table-name patterns that <see cref="TableSelection"/> applies to (exact <c>schema.table</c> names or <c>*</c> wildcards). Defaults to an empty list, which under <see cref="ExportTableSelectionMode.AllExcept"/> means export everything.
    /// </summary>
    public IList<string> Tables { get; set; } = new List<string>();

    /// <summary>
    /// Fully qualified column paths (<c>schema.table.column</c>) to omit from the exported package. Defaults to an empty list (export every column of every selected table).
    /// </summary>
    public IList<string> ExcludeColumns { get; set; } = new List<string>();

    /// <summary>
    /// SQL Server WHERE predicates applied to every selected table that contains the named column — useful for tenant or soft-delete filtering. Defaults to an empty list.
    /// </summary>
    public IList<GlobalWhereClause> GlobalWhereClauses { get; set; } = new List<GlobalWhereClause>();

    /// <summary>
    /// SQL Server WHERE predicates applied only to the specific tables they name. Defaults to an empty list.
    /// </summary>
    public IList<PerTableWhereClause> PerTableWhereClauses { get; set; } = new List<PerTableWhereClause>();

    /// <summary>
    /// Prefix prepended to every exported data table inside the SQLite package — keeps data tables separate from internal metadata tables. Defaults to <c>"zsb_data"</c>; set to <see langword="null"/> or empty to write tables without a prefix.
    /// </summary>
    public string? DataTablePrefix { get; set; } = "zsb_data";

    /// <summary>
    /// Row count per SQLite write batch for normal-sized tables. Defaults to <c>1000</c>; raise for narrow tables to reduce commit overhead, lower for very wide rows.
    /// </summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>
    /// Enables the large-table planner that shrinks batch sizes for tables exceeding <see cref="LargeTableThresholdBytes"/>/<see cref="LargeTableRowThreshold"/>, trading throughput for memory pressure. Defaults to <see langword="true"/>; set to <see langword="false"/> to always use <see cref="BatchSize"/>.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; set; } = true;

    /// <summary>
    /// Estimated table size, in bytes, at or above which a table is treated as "large" and switched to <see cref="LargeTableBatchSize"/>. Defaults to 50 MiB.
    /// </summary>
    public long LargeTableThresholdBytes { get; set; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Estimated row count at or above which a table is treated as "large" when size metadata is unavailable. Defaults to <c>100,000</c> rows.
    /// </summary>
    public long LargeTableRowThreshold { get; set; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Row count per batch used when a table crosses either large-table threshold. Defaults to <c>250</c>.
    /// </summary>
    public int LargeTableBatchSize { get; set; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Approximate upper bound, in bytes, on the in-memory size of a single batch when size and row-count metadata are both available — caps memory regardless of <see cref="BatchSize"/>. Defaults to 4 MiB.
    /// </summary>
    public long MaxBatchBytes { get; set; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// SQL Server command timeout, in seconds, for metadata queries and data reads during export. Defaults to <see langword="null"/> (use the provider's default, typically 30 seconds).
    /// </summary>
    public int? CommandTimeout { get; set; }

    /// <summary>
    /// Progress reporter that receives table- and row-level updates as the export runs. Defaults to <see langword="null"/> (no progress reporting).
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; set; }

    /// <summary>
    /// Allows the export to replace an existing SQLite file at the destination path on successful completion. Defaults to <see langword="false"/>; an existing file otherwise fails the export.
    /// </summary>
    public bool OverwriteExistingPackage { get; set; }

    /// <summary>
    /// Selects whether to embed source-schema information in the package. Defaults to <see cref="SchemaCaptureMode.None"/> (data-only package); set to <see cref="SchemaCaptureMode.Dacpac"/> to extract a dacpac during export.
    /// </summary>
    public SchemaCaptureMode SchemaCaptureMode { get; set; } = SchemaCaptureMode.None;

    /// <summary>
    /// Dacpac extraction settings used only when <see cref="SchemaCaptureMode"/> is <see cref="SchemaCaptureMode.Dacpac"/>. Defaults to a new <see cref="DacpacCaptureOptions"/> with its own defaults.
    /// </summary>
    public DacpacCaptureOptions DacpacCaptureOptions { get; set; } = new();
}

/// <summary>
/// Configures a SQLite package import into SQL Server.
/// </summary>
public sealed class ImportOptions
{
    /// <summary>
    /// Returns a new <see cref="ImportOptions"/> populated with the documented defaults — a convenient starting point to tweak.
    /// Each access returns a fresh instance, so mutating the returned object never affects subsequent callers.
    /// </summary>
    public static ImportOptions Default => new()
    {
        BatchSize = 1_000,
        AdaptiveBatchingEnabled = true,
        LargeTableThresholdBytes = BatchPlanner.DefaultLargeTableThresholdBytes,
        LargeTableRowThreshold = BatchPlanner.DefaultLargeTableRowThreshold,
        LargeTableBatchSize = BatchPlanner.DefaultLargeTableBatchSize,
        MaxBatchBytes = BatchPlanner.DefaultMaxBatchBytes,
        SchemaDeploymentMode = SchemaDeploymentMode.None,
        SuspendTemporalSystemVersioning = true,
        TemporalDataConsistencyCheck = true
    };

    /// <summary>
    /// Row count per bulk-copy batch for normal-sized tables. Defaults to <c>1000</c>.
    /// </summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>
    /// Enables the large-table planner that shrinks bulk-copy batch sizes for tables exceeding <see cref="LargeTableThresholdBytes"/>/<see cref="LargeTableRowThreshold"/>, trading throughput for lower memory pressure. Defaults to <see langword="true"/>; set to <see langword="false"/> to always use <see cref="BatchSize"/>.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; set; } = true;

    /// <summary>
    /// Estimated table size, in bytes, at or above which a table is treated as "large" and switched to <see cref="LargeTableBatchSize"/>. Defaults to 50 MiB.
    /// </summary>
    public long LargeTableThresholdBytes { get; set; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Estimated row count at or above which a table is treated as "large" when size metadata is unavailable. Defaults to <c>100,000</c> rows.
    /// </summary>
    public long LargeTableRowThreshold { get; set; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Row count per bulk-copy batch used when a table crosses either large-table threshold. Defaults to <c>250</c>.
    /// </summary>
    public int LargeTableBatchSize { get; set; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Approximate upper bound, in bytes, on the in-memory size of a single bulk-copy batch when size and row-count metadata are both available — caps memory regardless of <see cref="BatchSize"/>. Defaults to 4 MiB.
    /// </summary>
    public long MaxBatchBytes { get; set; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// SQL Server command timeout, in seconds, for the target validation queries run before bulk copy begins. Defaults to <see langword="null"/> (use the provider's default).
    /// </summary>
    public int? ValidationCommandTimeout { get; set; }

    /// <summary>
    /// Timeout, in seconds, for each <c>SqlBulkCopy</c> operation. Defaults to <see langword="null"/> (use <c>SqlBulkCopy</c>'s default of 30 seconds); raise this for very large or slow tables.
    /// </summary>
    public int? BulkCopyTimeout { get; set; }

    /// <summary>
    /// Progress reporter that receives table- and row-level updates as the import runs. Defaults to <see langword="null"/> (no progress reporting).
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; set; }

    /// <summary>
    /// Selects whether to deploy the package's embedded schema before loading data. Defaults to <see cref="SchemaDeploymentMode.None"/> (assume target schema already exists); set to <see cref="SchemaDeploymentMode.DeployDacpac"/> to apply the captured dacpac first.
    /// </summary>
    public SchemaDeploymentMode SchemaDeploymentMode { get; set; } = SchemaDeploymentMode.None;

    /// <summary>
    /// Dacpac deployment settings used only when <see cref="SchemaDeploymentMode"/> is <see cref="SchemaDeploymentMode.DeployDacpac"/>. Defaults to a new <see cref="DacpacDeploymentOptions"/> with its own defaults.
    /// </summary>
    public DacpacDeploymentOptions DacpacDeploymentOptions { get; set; } = new();

    /// <summary>
    /// Handles system-versioned temporal tables on the target by temporarily setting <c>SYSTEM_VERSIONING = OFF</c>
    /// and dropping the <c>SYSTEM_TIME</c> period before loading, then re-adding the period and re-enabling versioning.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Without this, importing a temporal table fails: SQL Server rejects direct inserts into the history table
    /// (Msg 13560) and into the <c>GENERATED ALWAYS</c> period columns of the current table (Msg 13536). The
    /// ceremony lets the import reload both the current and history rows with their original
    /// <c>ValidFrom</c>/<c>ValidTo</c> values, and re-applies a finite <c>HISTORY_RETENTION_PERIOD</c> (which
    /// <c>SET SYSTEM_VERSIONING = OFF</c> would otherwise reset to INFINITE). The period is dropped only when the
    /// package actually carries the period columns; when it does not (a non-temporal source loaded into a
    /// temporal target, or period columns excluded during export) versioning stays on and SQL Server
    /// auto-populates the period. Set to <see langword="false"/> to load every target table as-is and let those
    /// inserts fail loudly — useful when the target has no temporal tables and you want to skip the catalog
    /// probe, or when temporal handling is managed externally.
    /// </remarks>
    public bool SuspendTemporalSystemVersioning { get; set; } = true;

    /// <summary>
    /// When re-enabling system versioning after a temporal load, runs SQL Server's <c>DATA_CONSISTENCY_CHECK</c>
    /// to validate that the current and history period ranges do not overlap. Defaults to <see langword="true"/>.
    /// Only applies when <see cref="SuspendTemporalSystemVersioning"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A faithful full-table export passes the check. It fails (and aborts the import with a descriptive
    /// <see cref="BridgeException"/>) when the temporal data is inconsistent — for example when the temporal table
    /// or its history was filtered with a WHERE clause, a period column was excluded, or the source changed
    /// mid-export. Set to <see langword="false"/> to re-enable versioning without validation, at the risk of a
    /// temporal table that returns incorrect <c>AS OF</c> query results.
    /// </remarks>
    public bool TemporalDataConsistencyCheck { get; set; } = true;
}

/// <summary>
/// Shared compatibility options for callers that want to pass one options object to export and import.
/// Prefer <see cref="ExportOptions"/> and <see cref="ImportOptions"/> for new code.
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>
    /// Returns a new <see cref="BridgeOptions"/> populated with the documented defaults — a convenient starting point to tweak.
    /// Each access returns a fresh instance, so mutating the returned object never affects subsequent callers.
    /// </summary>
    public static BridgeOptions Default => new()
    {
        TableSelection = ExportTableSelectionMode.AllExcept,
        DataTablePrefix = "zsb_data",
        BatchSize = 1_000,
        AdaptiveBatchingEnabled = true,
        LargeTableThresholdBytes = BatchPlanner.DefaultLargeTableThresholdBytes,
        LargeTableRowThreshold = BatchPlanner.DefaultLargeTableRowThreshold,
        LargeTableBatchSize = BatchPlanner.DefaultLargeTableBatchSize,
        MaxBatchBytes = BatchPlanner.DefaultMaxBatchBytes,
        OverwriteExistingPackage = false,
        SchemaCaptureMode = SchemaCaptureMode.None,
        SchemaDeploymentMode = SchemaDeploymentMode.None,
        SuspendTemporalSystemVersioning = true,
        TemporalDataConsistencyCheck = true
    };

    /// <summary>
    /// Controls how <see cref="Tables"/> is interpreted during export: <see cref="ExportTableSelectionMode.AllExcept"/> excludes matches from a full export, <see cref="ExportTableSelectionMode.Only"/> exports just the matches. Defaults to <see cref="ExportTableSelectionMode.AllExcept"/>.
    /// </summary>
    public ExportTableSelectionMode TableSelection { get; set; } = ExportTableSelectionMode.AllExcept;

    /// <summary>
    /// Table-name patterns that <see cref="TableSelection"/> applies to during export (exact <c>schema.table</c> names or <c>*</c> wildcards). Defaults to an empty list.
    /// </summary>
    public IList<string> Tables { get; set; } = new List<string>();

    /// <summary>
    /// Fully qualified column paths (<c>schema.table.column</c>) to omit during export. Defaults to an empty list (export every column).
    /// </summary>
    public IList<string> ExcludeColumns { get; set; } = new List<string>();

    /// <summary>
    /// SQL Server WHERE predicates applied to every selected table that contains the named column — useful for tenant or soft-delete filtering. Defaults to an empty list.
    /// </summary>
    public IList<GlobalWhereClause> GlobalWhereClauses { get; set; } = new List<GlobalWhereClause>();

    /// <summary>
    /// SQL Server WHERE predicates applied only to the specific export tables they name. Defaults to an empty list.
    /// </summary>
    public IList<PerTableWhereClause> PerTableWhereClauses { get; set; } = new List<PerTableWhereClause>();

    /// <summary>
    /// Prefix prepended to every exported data table inside the SQLite package — separates data tables from internal metadata tables. Defaults to <c>"zsb_data"</c>; set to <see langword="null"/> or empty to omit the prefix.
    /// </summary>
    public string? DataTablePrefix { get; set; } = "zsb_data";

    /// <summary>
    /// Row count per batch used by both export writes and import bulk-copy on normal-sized tables. Defaults to <c>1000</c>.
    /// </summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>
    /// Enables the large-table planner that shrinks batch sizes for tables exceeding <see cref="LargeTableThresholdBytes"/>/<see cref="LargeTableRowThreshold"/>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AdaptiveBatchingEnabled { get; set; } = true;

    /// <summary>
    /// Estimated table size, in bytes, at or above which a table is treated as "large" and switched to <see cref="LargeTableBatchSize"/>. Defaults to 50 MiB.
    /// </summary>
    public long LargeTableThresholdBytes { get; set; } = BatchPlanner.DefaultLargeTableThresholdBytes;

    /// <summary>
    /// Estimated row count at or above which a table is treated as "large" when size metadata is unavailable. Defaults to <c>100,000</c> rows.
    /// </summary>
    public long LargeTableRowThreshold { get; set; } = BatchPlanner.DefaultLargeTableRowThreshold;

    /// <summary>
    /// Row count per batch used when a table crosses either large-table threshold. Defaults to <c>250</c>.
    /// </summary>
    public int LargeTableBatchSize { get; set; } = BatchPlanner.DefaultLargeTableBatchSize;

    /// <summary>
    /// Approximate upper bound, in bytes, on the in-memory size of a single batch when size and row-count metadata are both available. Defaults to 4 MiB.
    /// </summary>
    public long MaxBatchBytes { get; set; } = BatchPlanner.DefaultMaxBatchBytes;

    /// <summary>
    /// SQL Server command timeout, in seconds, for metadata and data-read commands during export. Defaults to <see langword="null"/> (use the provider's default).
    /// </summary>
    public int? ExportCommandTimeout { get; set; }

    /// <summary>
    /// SQL Server command timeout, in seconds, for the target validation queries run before bulk copy begins during import. Defaults to <see langword="null"/> (use the provider's default).
    /// </summary>
    public int? ImportValidationCommandTimeout { get; set; }

    /// <summary>
    /// Timeout, in seconds, for each <c>SqlBulkCopy</c> operation during import. Defaults to <see langword="null"/> (use <c>SqlBulkCopy</c>'s 30-second default).
    /// </summary>
    public int? ImportBulkCopyTimeout { get; set; }

    /// <summary>
    /// Progress reporter that receives table- and row-level updates for both export and import. Defaults to <see langword="null"/>.
    /// </summary>
    public IProgress<BridgeProgress>? Progress { get; set; }

    /// <summary>
    /// Allows the export to replace an existing SQLite file at the destination path on successful completion. Defaults to <see langword="false"/>; an existing file otherwise fails the export.
    /// </summary>
    public bool OverwriteExistingPackage { get; set; }

    /// <summary>
    /// Selects whether export embeds source-schema information in the package. Defaults to <see cref="SchemaCaptureMode.None"/>; set to <see cref="SchemaCaptureMode.Dacpac"/> to extract a dacpac during export.
    /// </summary>
    public SchemaCaptureMode SchemaCaptureMode { get; set; } = SchemaCaptureMode.None;

    /// <summary>
    /// Dacpac extraction settings used only when <see cref="SchemaCaptureMode"/> is <see cref="SchemaCaptureMode.Dacpac"/>. Defaults to a new <see cref="DacpacCaptureOptions"/> with its own defaults.
    /// </summary>
    public DacpacCaptureOptions DacpacCaptureOptions { get; set; } = new();

    /// <summary>
    /// Selects whether import deploys the package's embedded schema before loading data. Defaults to <see cref="SchemaDeploymentMode.None"/>; set to <see cref="SchemaDeploymentMode.DeployDacpac"/> to apply the captured dacpac first.
    /// </summary>
    public SchemaDeploymentMode SchemaDeploymentMode { get; set; } = SchemaDeploymentMode.None;

    /// <summary>
    /// Dacpac deployment settings used only when <see cref="SchemaDeploymentMode"/> is <see cref="SchemaDeploymentMode.DeployDacpac"/>. Defaults to a new <see cref="DacpacDeploymentOptions"/> with its own defaults.
    /// </summary>
    public DacpacDeploymentOptions DacpacDeploymentOptions { get; set; } = new();

    /// <summary>
    /// On import, handles system-versioned temporal tables by temporarily suspending system versioning (and dropping the
    /// <c>SYSTEM_TIME</c> period) so the current and history rows can be loaded with their original period values, then
    /// restoring it. Defaults to <see langword="true"/>. See <see cref="ImportOptions.SuspendTemporalSystemVersioning"/>.
    /// </summary>
    public bool SuspendTemporalSystemVersioning { get; set; } = true;

    /// <summary>
    /// On import, runs SQL Server's <c>DATA_CONSISTENCY_CHECK</c> when re-enabling system versioning after a temporal load.
    /// Defaults to <see langword="true"/>. See <see cref="ImportOptions.TemporalDataConsistencyCheck"/>.
    /// </summary>
    public bool TemporalDataConsistencyCheck { get; set; } = true;

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
            DacpacDeploymentOptions = DacpacDeploymentOptions,
            SuspendTemporalSystemVersioning = SuspendTemporalSystemVersioning,
            TemporalDataConsistencyCheck = TemporalDataConsistencyCheck
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
