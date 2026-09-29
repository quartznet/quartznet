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
using Quartz.Weasel.Oracle;

namespace Quartz;

/// <summary>
/// Hands an Oracle job store's schema to Weasel.
/// </summary>
public static class OracleWeaselStoreBuilderExtensions
{
    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on Oracle through Oracle.ManagedDataAccess — <c>UseOracle(...)</c> on the same
    /// store — and must not also call <c>ProvisionSchema()</c>; both are checked at startup. Weasel reaches
    /// the database through the store's own connections, and finds the tables through its table prefix: a
    /// schema before the last dot, the session's current schema otherwise.
    /// </para>
    /// <para>
    /// The tables are add-only: columns, indexes and foreign keys the application added to them are kept.
    /// The index names Quartz 3.x created and 4.x retired are dropped. No lock is taken: nodes applying at
    /// once each read the schema again after a statement another one got to first, and stop when it is
    /// done.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="OracleWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForOracle(this IPersistentStoreBuilder store, Action<OracleWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        OracleWeaselOptions options = new();
        configure?.Invoke(options);

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = OracleQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new OracleQuartzDatabase(context),
        });

        return store;
    }
}
