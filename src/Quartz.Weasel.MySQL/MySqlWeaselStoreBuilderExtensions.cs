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
using Quartz.Weasel.MySQL;

namespace Quartz;

/// <summary>
/// Hands a MySQL job store's schema to Weasel.
/// </summary>
public static class MySqlWeaselStoreBuilderExtensions
{
    /// <summary>
    /// The longest name <c>GET_LOCK</c> accepts.
    /// </summary>
    private const int MaxLockNameLength = 64;

    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on MySQL through MySqlConnector — <c>UseMySqlConnector(...)</c> on the same store,
    /// because Weasel speaks no other MySQL driver — and must not also call <c>ProvisionSchema()</c>; both
    /// are checked at startup. Weasel reaches the database through the store's own connections, and finds
    /// the tables through its table prefix: a database before the last dot, the connection's database
    /// otherwise.
    /// </para>
    /// <para>
    /// The tables are add-only: columns, indexes and foreign keys the application added to them are kept.
    /// The index names Quartz 3.x created and 4.x retired are dropped. Every apply takes the user lock
    /// <see cref="MySqlWeaselOptions.LockName" /> first. MySQL 8.0 or later: 5.7 ignores the descending
    /// column of <c>IDX_QRTZ_T_NFT_ST</c>, so the index would read as changed on every apply.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="MySqlWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForMySql(this IPersistentStoreBuilder store, Action<MySqlWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        MySqlWeaselOptions options = new();
        configure?.Invoke(options);

        if (options.LockTimeout <= TimeSpan.Zero)
        {
            throw new SchedulerConfigException(
                $"MySqlWeaselOptions.LockTimeout is {options.LockTimeout}; it has to be positive, because it is how long an"
                + " apply waits for another session that holds the lock.");
        }

        if (string.IsNullOrEmpty(options.LockName) || options.LockName.Length > MaxLockNameLength)
        {
            throw new SchedulerConfigException(
                $"MySqlWeaselOptions.LockName is '{options.LockName}'; GET_LOCK takes a name of 1 to"
                + $" {MaxLockNameLength} characters.");
        }

        string lockName = options.LockName;
        TimeSpan lockTimeout = options.LockTimeout;

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = MySqlQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new MySqlQuartzDatabase(context, lockName, lockTimeout),
        });

        return store;
    }
}
