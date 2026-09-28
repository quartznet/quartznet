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

using Quartz.Weasel;
using Quartz.Weasel.SqlServer;

namespace Quartz;

/// <summary>
/// Hands a SQL Server job store's schema to Weasel.
/// </summary>
public static class SqlServerWeaselStoreBuilderExtensions
{
    /// <summary>
    /// The longest resource name <c>sp_getapplock</c> accepts.
    /// </summary>
    private const int MaxLockResourceLength = 255;

    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on SQL Server through Microsoft.Data.SqlClient — <c>UseSqlServer(...)</c> on the
    /// same store — and must not also call <c>ProvisionSchema()</c>; both are checked at startup. Weasel
    /// reaches the database through the store's own connections, and finds the tables through its table
    /// prefix: a schema before the last dot, <c>dbo</c> otherwise.
    /// </para>
    /// <para>
    /// The tables are add-only: columns, indexes and foreign keys the application added to them are kept.
    /// The index names Quartz 3.x created and 4.x retired are dropped. Every apply takes the application
    /// lock <see cref="SqlServerWeaselOptions.LockResource" /> first. A memory-optimized schema is refused:
    /// Weasel manages disk-based tables only.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="SqlServerWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForSqlServer(this IPersistentStoreBuilder store, Action<SqlServerWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        SqlServerWeaselOptions options = new();
        configure?.Invoke(options);

        if (options.LockTimeout <= TimeSpan.Zero)
        {
            throw new SchedulerConfigException(
                $"SqlServerWeaselOptions.LockTimeout is {options.LockTimeout}; it has to be positive, because it is how long an"
                + " apply waits for another session that holds the lock.");
        }

        if (string.IsNullOrEmpty(options.LockResource) || options.LockResource.Length > MaxLockResourceLength)
        {
            throw new SchedulerConfigException(
                $"SqlServerWeaselOptions.LockResource is '{options.LockResource}'; sp_getapplock takes a resource name of 1 to"
                + $" {MaxLockResourceLength} characters.");
        }

        string lockResource = options.LockResource;
        TimeSpan lockTimeout = options.LockTimeout;

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = SqlServerQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new SqlServerQuartzDatabase(context, lockResource, lockTimeout),
        });

        return store;
    }
}
