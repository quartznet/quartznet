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
using Quartz.Weasel.PostgreSQL;

namespace Quartz;

/// <summary>
/// Hands a PostgreSQL job store's schema to Weasel.
/// </summary>
public static class PostgresWeaselStoreBuilderExtensions
{
    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on PostgreSQL — <c>UsePostgres(...)</c> on the same store — and must not also call
    /// <c>ProvisionSchema()</c>; both are checked at startup. Weasel reaches the database through the
    /// store's own connections, and finds the tables through its table prefix: a schema before the last
    /// dot, <c>public</c> otherwise.
    /// </para>
    /// <para>
    /// The tables are add-only: columns, indexes and foreign keys the application added to them are kept.
    /// The index names Quartz 3.x created and 4.x retired are dropped. Every apply takes the advisory lock
    /// <see cref="PostgresWeaselOptions.LockId" /> first.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="PostgresWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForPostgres(this IPersistentStoreBuilder store, Action<PostgresWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        PostgresWeaselOptions options = new();
        configure?.Invoke(options);

        if (options.LockTimeout <= TimeSpan.Zero)
        {
            throw new SchedulerConfigException(
                $"PostgresWeaselOptions.LockTimeout is {options.LockTimeout}; it has to be positive, because it is how long an"
                + " apply waits for another process that holds the lock.");
        }

        int lockId = options.LockId;
        TimeSpan lockTimeout = options.LockTimeout;

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = PostgresQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new PostgresQuartzDatabase(context, lockId, lockTimeout),
        });

        return store;
    }
}
