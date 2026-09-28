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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Weasel;
using Quartz.Weasel.PostgreSQL;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Quartz;

/// <summary>
/// Quartz.NET's job store tables as a Weasel feature schema, for adding to a Marten store so that
/// Marten applies them with the rest of its schema.
/// </summary>
/// <remarks>
/// <para>
/// This is the join mode: <c>opts.Storage.Add(QuartzPostgresFeatureSchema.ForScheduler(services))</c>
/// inside <c>ConfigureMarten</c>. Marten then creates and migrates Quartz's tables whenever it applies
/// its own — at startup, in <c>db-apply</c>, in <c>resources setup</c> — and Quartz registers no
/// database of its own. Call either this or <c>UseWeaselForPostgres</c> for one scheduler, never both.
/// </para>
/// <para>
/// A feature schema rather than <c>StoreOptions.Storage.ExtendedSchemaObjects</c>: Marten's
/// <c>CompletelyRemoveAllAsync</c> drops every extended schema object with <c>CASCADE</c>, which would
/// take the schedule with it; a feature schema is left alone.
/// </para>
/// <para>
/// The tables are the same objects <c>UseWeaselForPostgres</c> applies: add-only, spelled the way
/// PostgreSQL's catalog reads them back, and with the index names 3.x created and 4.x retired dropped.
/// </para>
/// </remarks>
public sealed class QuartzPostgresFeatureSchema : IFeatureSchema
{
    private readonly ISchemaObject[] objects;

    /// <summary>
    /// The tables for a table prefix, spelled as <see cref="AdoJobStoreOptions.TablePrefix" /> spells it.
    /// </summary>
    /// <param name="tablePrefix">
    /// The store's table prefix; a schema before the last dot is the schema the tables are in, and
    /// <c>public</c> is used when there is none.
    /// </param>
    public QuartzPostgresFeatureSchema(string tablePrefix = "QRTZ_")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePrefix);

        (string? schema, string prefix) = QuartzWeaselSystemPart.SplitTablePrefix(tablePrefix);
        objects = [.. QuartzTables.Build(new QuartzTableNaming(schema ?? PostgresQuartzDatabase.Dialect.DefaultSchema, prefix))];
    }

    /// <summary>
    /// The tables of a scheduler registered with <c>AddQuartz</c>, read from its store's table prefix.
    /// </summary>
    /// <param name="services">The application's services, as <c>ConfigureMarten((services, opts) =&gt; …)</c> hands them over.</param>
    /// <param name="schedulerName">The scheduler's registration name, or <see langword="null" /> for the default scheduler.</param>
    /// <exception cref="SchedulerConfigException">
    /// The scheduler's schema is already managed standalone, by <c>UseWeaselForPostgres</c>.
    /// </exception>
    public static QuartzPostgresFeatureSchema ForScheduler(IServiceProvider services, string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        string? key = string.IsNullOrEmpty(schedulerName) ? null : schedulerName;

        if (services.GetServices<QuartzWeaselRegistration>().Any(x => string.Equals(x.SchedulerKey, key, StringComparison.Ordinal)))
        {
            throw new SchedulerConfigException(
                $"The schema of {(key is null ? "the default scheduler" : $"scheduler '{key}'")} is already managed by"
                + $" {PostgresQuartzDatabase.Dialect.RegistrationMethod}(), and adding it to a Marten store as well would give it"
                + " two owners. Remove one: UseWeaselForPostgres() to let Marten apply it, or the Storage.Add(...) line to"
                + " keep it standalone.");
        }

        AdoJobStoreOptions store = services.GetRequiredService<IOptionsMonitor<AdoJobStoreOptions>>().Get(key ?? Options.DefaultName);
        return new QuartzPostgresFeatureSchema(store.TablePrefix);
    }

    /// <summary>
    /// The model as <see cref="PostgresQuartzDatabase" /> builds it, from a prefix already split.
    /// </summary>
    internal QuartzPostgresFeatureSchema(QuartzTableNaming naming)
    {
        objects = [.. QuartzTables.Build(naming)];
    }

    /// <inheritdoc />
    public ISchemaObject[] Objects => objects;

    /// <inheritdoc />
    public string Identifier => "quartz";

    /// <inheritdoc />
    public Migrator Migrator { get; } = PostgresQuartzDatabase.CreateMigrator();

    /// <inheritdoc />
    public Type StorageType => typeof(QuartzPostgresFeatureSchema);

    /// <inheritdoc />
    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing: the tables need no grants beyond what creating them gives.
    }

    /// <inheritdoc />
    public IEnumerable<Type> DependentTypes() => Type.EmptyTypes;
}
