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

using FirebirdSql.Data.FirebirdClient;

using JasperFx;
using JasperFx.Descriptors;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Firebird;
using Weasel.Firebird.Tables;

namespace Quartz.Weasel.Firebird;

/// <summary>
/// One scheduler's Firebird schema as a Weasel database, reached through the store's own connections.
/// </summary>
/// <remarks>
/// <para>
/// There is no global lock, as on SQLite: Weasel.Firebird ships none, because a lock held in a
/// transaction on the migration's own connection would break the one-transaction-per-statement way it
/// applies DDL. Every statement it runs is guarded instead, and a guarded statement that loses a
/// catalog race is run again by Weasel itself. What is left — a failure that outlasts those retries —
/// is answered the way <c>ProvisionSchema()</c> answers a lost race: by reading the schema again, and
/// finding that nothing is left to do. <see cref="IDatabase.ApplyAllConfiguredChangesToDatabaseAsync" />
/// is re-implemented here for that.
/// </para>
/// <para>
/// Every name the model would create is checked against the identifier limit when the database is
/// built, before any command runs, so a table prefix too long for Firebird 3 is refused with the one
/// setting that changes it.
/// </para>
/// </remarks>
internal sealed class FirebirdQuartzDatabase : DatabaseBase<FbConnection>, IQuartzWeaselDatabase
{
    internal static readonly QuartzWeaselDialect Dialect = new()
    {
        DatabaseName = "Firebird",
        RegistrationMethod = "UseWeaselForFirebird",
        StoreMethod = "UseFirebird",
        DefaultSchema = FirebirdObjectName.DefaultSchema,
        AcceptsConnection = static connection => connection is FbConnection,
    };

    /// <summary>
    /// How much longer than the table prefix the longest name the model creates is:
    /// <c>IDX_&lt;prefix&gt;FT_INST_JOB_REQ_RCVRY</c>.
    /// </summary>
    internal const int LongestNameBeyondThePrefix = 25;

    /// <summary>How many times an apply that failed is read again and retried before the failure stands.</summary>
    private const int ApplyAttempts = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IFeatureSchema feature;
    private readonly TimeProvider timeProvider;

    public FirebirdQuartzDatabase(QuartzWeaselDatabaseContext context, int maxIdentifierLength, TimeProvider? timeProvider = null)
        : base(
            context.MigrationLogger,
            AutoCreate.CreateOrUpdate,
            CreateMigrator(maxIdentifierLength),
            context.SchedulerName,
            () => (FbConnection) context.DbProvider.CreateConnection())
    {
        Context = context;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        if (!FirebirdObjectName.IsDefaultSchema(context.Schema))
        {
            throw new SchedulerConfigException(
                $"Scheduler '{context.SchedulerName}' has a table prefix naming schema '{context.Schema}', and Firebird 3, 4"
                + " and 5 have no schemas. Use a table prefix without a dot.");
        }

        ISchemaObject[] objects = [.. QuartzTables.Build(new QuartzTableNaming(context.Schema, context.TablePrefix))];
        ThrowIfANameIsTooLong(objects, (FirebirdMigrator) Migrator, context, maxIdentifierLength);

        feature = new QuartzFirebirdFeatureSchema(objects, maxIdentifierLength);

        DatabaseDescriptor descriptor = Describe();
        Id = new DatabaseId(descriptor.ServerName, descriptor.DatabaseName);
    }

    public QuartzWeaselDatabaseContext Context { get; }

    /// <summary>
    /// Refuses a change it could only make by dropping and recreating a table, under every
    /// <see cref="AutoCreate" /> — including <see cref="AutoCreate.All" />, which would otherwise allow it.
    /// </summary>
    private static FirebirdMigrator CreateMigrator(int maxIdentifierLength) => new()
    {
        RefuseDestructiveChanges = true,
        MaxIdentifierLength = maxIdentifierLength,
    };

    public override IFeatureSchema[] BuildFeatureSchemas() => [feature];

    public override DatabaseDescriptor Describe()
    {
        FbConnectionStringBuilder builder = new(Context.ConnectionString);

        return new DatabaseDescriptor
        {
            Engine = FirebirdProvider.EngineName,
            ServerName = builder.DataSource ?? "",
            DatabaseName = builder.Database ?? "",
            SchemaOrNamespace = Context.Schema,
            Subject = typeof(FirebirdQuartzDatabase).FullName!,
            Identifier = Context.SchedulerName,
            SubjectUri = Context.SubjectUri,
        };
    }

    /// <summary>
    /// Nothing: the pool is the scheduler's, and <c>db-apply</c> releasing it would close the
    /// connections of a scheduler running in the same process.
    /// </summary>
    public override ValueTask ReleaseConnectionPoolAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    Task<SchemaPatchDifference> IDatabase.ApplyAllConfiguredChangesToDatabaseAsync(
        AutoCreate? @override,
        ReconnectionOptions? reconnectionOptions,
        CancellationToken ct) =>
        LockFreeApply.ApplyAsync(
            this,
            cancellationToken => ApplyAllConfiguredChangesToDatabaseAsync(@override, reconnectionOptions, cancellationToken),
            ApplyAttempts,
            RetryDelay,
            timeProvider,
            ct);

    /// <summary>
    /// Refuses a model with a name longer than the identifier limit, naming the name and the setting
    /// that changes the limit.
    /// </summary>
    /// <remarks>
    /// Weasel checks the same names when it writes DDL, which is also before anything runs; this says
    /// it in Quartz's terms, where the remedy is a table prefix or <see cref="FirebirdWeaselOptions" />
    /// rather than a property of Weasel's migrator. The retired index names are left out: they are
    /// never created, and one too long for the limit cannot be in the database to drop.
    /// </remarks>
    private static void ThrowIfANameIsTooLong(
        IEnumerable<ISchemaObject> objects,
        FirebirdMigrator migrator,
        QuartzWeaselDatabaseContext context,
        int maxIdentifierLength)
    {
        foreach (Table table in objects.OfType<Table>())
        {
            IEnumerable<string> names = table.AllNames().Select(x => x.Name).Concat(table.LocalIdentifiers());

            foreach (string name in names)
            {
                try
                {
                    migrator.AssertValidIdentifier(name);
                }
                catch (InvalidOperationException e)
                {
                    string remedy = maxIdentifierLength < FirebirdWeaselOptions.Firebird4MaxIdentifierLength
                        ? $" Use a table prefix of at most {maxIdentifierLength - LongestNameBeyondThePrefix} characters, or, for a database"
                          + $" only Firebird 4 or later opens, set FirebirdWeaselOptions.MaxIdentifierLength to {FirebirdWeaselOptions.Firebird4MaxIdentifierLength}."
                        : $" Use a table prefix of at most {maxIdentifierLength - LongestNameBeyondThePrefix} characters.";

                    throw new SchedulerConfigException(
                        $"Scheduler '{context.SchedulerName}' has the table prefix '{context.TablePrefix}', which makes the Firebird"
                        + $" name {name} longer than the {maxIdentifierLength} allowed. Nothing was changed.{remedy}",
                        e);
                }
            }
        }
    }

    /// <summary>The schema's objects as the one feature the database has.</summary>
    private sealed class QuartzFirebirdFeatureSchema : FeatureSchemaBase
    {
        private readonly ISchemaObject[] objects;

        public QuartzFirebirdFeatureSchema(ISchemaObject[] objects, int maxIdentifierLength)
            : base("quartz", CreateMigrator(maxIdentifierLength))
        {
            this.objects = objects;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects() => objects;
    }
}
