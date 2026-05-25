using Microsoft.Data.Sqlite;
using Zachtbeer.SqlDataBridge.Internal;

namespace Zachtbeer.SqlDataBridge.Models;

/// <summary>
/// Reads metadata from a Zachtbeer.SqlDataBridge SQLite package without importing rows.
/// </summary>
public sealed class DataPackageReader
{
    /// <summary>
    /// Reads the package manifest from a SQLite package.
    /// </summary>
    /// <param name="sqliteFilePath">The SQLite package path.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The package manifest.</returns>
    public async Task<BridgePackageManifest> ReadManifestAsync(string sqliteFilePath, CancellationToken cancellationToken = default)
    {
        var sqliteBuilder = new SqliteConnectionStringBuilder { DataSource = sqliteFilePath, Mode = SqliteOpenMode.ReadOnly };
        await using var sqlite = new SqliteConnection(sqliteBuilder.ConnectionString);
        try
        {
            await sqlite.OpenAsync(cancellationToken);

            await SqlitePackage.ValidateForImportAsync(sqlite, cancellationToken);
            return await SqlitePackage.ReadManifestAsync(sqlite, cancellationToken);
        }
        finally
        {
            try { SqliteConnection.ClearPool(sqlite); } catch { /* best effort */ }
        }
    }
}
