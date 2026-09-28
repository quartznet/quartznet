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
using Quartz.Weasel.SQLite;

namespace Quartz;

/// <summary>
/// Hands a SQLite job store's schema to Weasel.
/// </summary>
public static class SqliteWeaselStoreBuilderExtensions
{
    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on SQLite through Microsoft.Data.Sqlite — <c>UseSqlite(...)</c> on the same store,
    /// which is the driver Weasel speaks — and must not also call <c>ProvisionSchema()</c>; both are checked
    /// at startup. Weasel reaches the database through the store's own connections.
    /// </para>
    /// <para>
    /// The tables are add-only: columns and indexes the application added to them are kept. SQLite makes
    /// some changes by rebuilding a table, which would not keep them, so a migration that would rebuild a
    /// table carrying any is refused, naming them, before anything runs.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="SqliteWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForSqlite(this IPersistentStoreBuilder store, Action<SqliteWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        SqliteWeaselOptions options = new();
        configure?.Invoke(options);

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = SqliteQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new SqliteQuartzDatabase(context),
        });

        return store;
    }
}
