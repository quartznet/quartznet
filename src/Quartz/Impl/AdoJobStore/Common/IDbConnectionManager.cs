using System.Data.Common;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Manages a collection of <see cref="IDbProvider" />s, and provides transparent access
/// to their database.
/// </summary>
public interface IDbConnectionManager
{
    /// <summary>
    /// Shuts down database connections from the data source with the given name,
    /// if applicable for the underlying provider.
    /// </summary>
    void Shutdown(string dataSourceName);

    /// <summary>
    /// Get a database connection from the data source with the given name.
    /// </summary>
    DbConnection GetConnection(string dataSourceName);

    /// <summary>
    /// Returns meta data for data source with the given name.
    /// </summary>
    DbMetadata GetDbMetadata(string dataSourceName);

    /// <summary>
    /// Gets the db provider registered as the data source with the given name.
    /// </summary>
    IDbProvider GetDbProvider(string dataSourceName);

    /// <summary>
    /// Registers a db provider as the data source with the given name.
    /// </summary>
    void AddDbProvider(string dataSourceName, IDbProvider provider);
}
