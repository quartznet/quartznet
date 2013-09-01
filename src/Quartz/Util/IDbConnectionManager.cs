using System.Data;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Util
{
    /// <summary>
    /// Manages a collection of IDbProviders, and provides transparent access
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
        IDbConnection GetConnection(string dataSourceName);

        /// <summary>
        /// Returns meta data for data source with the given name.
        /// </summary>
        DbMetadata GetDbMetadata(string dataSourceName);

        /// <summary>
        /// Gets db provider for data source with the given name.
        /// </summary>
        IDbProvider GetDbProvider(string dataSourceName);

        /// <summary>
        /// Adds a connection provider to data source with the given name.
        /// </summary>
        void AddConnectionProvider(string dataSourceName, IDbProvider provider);
    }
}