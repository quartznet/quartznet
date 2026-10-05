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
using Quartz.Weasel.Firebird;

namespace Quartz;

/// <summary>
/// Hands a Firebird job store's schema to Weasel.
/// </summary>
public static class FirebirdWeaselStoreBuilderExtensions
{
    /// <summary>
    /// Lets Weasel create and migrate this store's tables: at startup, and from JasperFx's
    /// <c>db-apply</c>, <c>db-assert</c>, <c>db-patch</c> and <c>resources</c> commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store must be on Firebird through FirebirdSql.Data.FirebirdClient — <c>UseFirebird(...)</c> on
    /// the same store, which is the driver Weasel speaks — and must not also call
    /// <c>ProvisionSchema()</c>; both are checked at startup. Weasel reaches the database through the
    /// store's own connections. Firebird 3, 4 and 5 have no schemas, so a table prefix naming one is
    /// refused.
    /// </para>
    /// <para>
    /// The tables are add-only: columns, indexes and foreign keys the application added to them are kept.
    /// The index names Quartz 3.x created and 4.x retired are dropped. No lock is taken: every statement
    /// Weasel runs is guarded, and nodes applying at once read the schema again and stop when it is done.
    /// </para>
    /// <para>
    /// The dialect is in the name so that an application referencing more than one Quartz.Weasel package
    /// never has two candidates for one call.
    /// </para>
    /// </remarks>
    /// <param name="store">The persistent store being configured.</param>
    /// <param name="configure">Adjusts <see cref="FirebirdWeaselOptions" />.</param>
    /// <returns>The same store builder, for chaining.</returns>
    public static IPersistentStoreBuilder UseWeaselForFirebird(this IPersistentStoreBuilder store, Action<FirebirdWeaselOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        FirebirdWeaselOptions options = new();
        configure?.Invoke(options);

        if (options.MaxIdentifierLength is < FirebirdWeaselOptions.Firebird3MaxIdentifierLength or > FirebirdWeaselOptions.Firebird4MaxIdentifierLength)
        {
            throw new SchedulerConfigException(
                $"FirebirdWeaselOptions.MaxIdentifierLength is {options.MaxIdentifierLength}; it is"
                + $" {FirebirdWeaselOptions.Firebird3MaxIdentifierLength} for Firebird 3, or up to"
                + $" {FirebirdWeaselOptions.Firebird4MaxIdentifierLength} for a database only Firebird 4 or later opens.");
        }

        int maxIdentifierLength = options.MaxIdentifierLength;

        QuartzWeaselRegistrar.Register(store, new QuartzWeaselRegistration
        {
            SchedulerKey = string.IsNullOrEmpty(store.SchedulerName) ? null : store.SchedulerName,
            Dialect = FirebirdQuartzDatabase.Dialect,
            AutoCreate = options.AutoCreate,
            CreateDatabase = context => new FirebirdQuartzDatabase(context, maxIdentifierLength),
        });

        return store;
    }
}
