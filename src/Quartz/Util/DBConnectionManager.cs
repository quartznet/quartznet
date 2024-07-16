#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Collections.Concurrent;
using System.Data.Common;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Diagnostics;

namespace Quartz.Util;

/// <summary>
/// Manages a collection of IDbProviders, and provides transparent access
/// to their database.
/// </summary>
/// <seealso cref="IDbProvider" />
/// <author>James House</author>
/// <author>Sharada Jambula</author>
/// <author>Mohammad Rezaei</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class DBConnectionManager : IDbConnectionManager
{
    private static readonly DBConnectionManager instance = new DBConnectionManager();
    private readonly ILogger<DBConnectionManager> logger;

    private readonly ConcurrentDictionary<string, IDbProvider> providers = new ConcurrentDictionary<string, IDbProvider>();

    /// <summary>
    /// Get the class instance.
    /// </summary>
    /// <returns> an instance of this class
    /// </returns>
    public static IDbConnectionManager Instance => instance;

    /// <summary>
    /// Private constructor
    /// </summary>
    private DBConnectionManager()
    {
        logger = LogProvider.CreateLogger<DBConnectionManager>();
    }

    /// <summary>
    /// Adds the connection provider.
    /// </summary>
    /// <param name="dataSourceName">Name of the data source.</param>
    /// <param name="provider">The provider.</param>
    public void AddConnectionProvider(string dataSourceName, IDbProvider provider)
    {
        logger.LogInformation("Registering datasource '{DataSource}' with db provider: '{Provider}'", dataSourceName, provider);

        providers[dataSourceName] = provider;
    }

    /// <summary>
    /// Get a database connection from the DataSource with the given name.
    /// </summary>
    /// <returns> a database connection </returns>
    public DbConnection GetConnection(string dataSourceName)
    {
        var provider = GetDbProvider(dataSourceName);
        return provider.CreateConnection();
    }

    /// <summary>
    /// Shuts down database connections from the DataSource with the given name,
    /// if applicable for the underlying provider.
    /// </summary>
    public void Shutdown(string dsName)
    {
        IDbProvider provider = GetDbProvider(dsName);
        provider.Shutdown();
    }

    public DbMetadata GetDbMetadata(string dsName)
    {
        return GetDbProvider(dsName).Metadata;
    }

    /// <summary>
    /// Gets the db provider.
    /// </summary>
    /// <param name="dsName">Name of the ds.</param>
    /// <returns></returns>
    public IDbProvider GetDbProvider(string dsName)
    {
        if (string.IsNullOrEmpty(dsName))
        {
            ThrowHelper.ThrowArgumentException("DataSource name cannot be null or empty", nameof(dsName));
        }

        if (!providers.TryGetValue(dsName, out IDbProvider? provider))
        {
            ThrowHelper.ThrowArgumentException($"There is no DataSource named '{dsName}'", nameof(dsName));
        }

        return provider;
    }
}