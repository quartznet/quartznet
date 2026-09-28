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

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using JasperFx.CommandLine;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Descriptors;
using JasperFx.Environment;
using JasperFx.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore.Common;

using Weasel.Core.CommandLine;
using Weasel.Core.Migrations;

namespace Quartz.Weasel;

/// <summary>
/// The Quartz schedulers whose schema Weasel manages, as JasperFx's command line and resource model
/// see them: one Weasel database per scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="IDatabaseWithRewindableState" /> on any of the databases:
/// <c>resources clear</c> and JasperFx's <c>ResetState</c> would take that as permission to empty the
/// tables, and Quartz's tables hold the schedule itself rather than state that can be rebuilt.
/// </para>
/// <para>
/// The databases are built on first use and kept, so a misconfigured scheduler — a store on another
/// database, or a store that also provisions its own schema — fails the first command or the startup
/// that needs it, naming the scheduler.
/// </para>
/// </remarks>
internal sealed class QuartzWeaselSystemPart : ISystemPart, IDatabaseSource
{
    private readonly IServiceProvider services;
    private readonly QuartzWeaselRegistration[] registrations;
    private readonly Lock gate = new();
    private IReadOnlyList<IQuartzWeaselDatabase>? databases;

    public QuartzWeaselSystemPart(IEnumerable<QuartzWeaselRegistration> registrations, IServiceProvider services)
    {
        this.registrations = registrations.ToArray();
        this.services = services;
    }

    public string Title => "Quartz.NET";

    public Uri SubjectUri { get; } = new("quartz://");

    public DatabaseCardinality Cardinality =>
        registrations.Length > 1 ? DatabaseCardinality.StaticMultiple : DatabaseCardinality.Single;

    /// <summary>One database per scheduler that asked for Weasel, built on first use.</summary>
    internal IReadOnlyList<IQuartzWeaselDatabase> Databases
    {
        get
        {
            lock (gate)
            {
                return databases ??= registrations.Select(Build).ToArray();
            }
        }
    }

    public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases() =>
        ValueTask.FromResult<IReadOnlyList<IDatabase>>([.. Databases]);

    public ValueTask<IReadOnlyList<IStatefulResource>> FindResources() =>
        ValueTask.FromResult<IReadOnlyList<IStatefulResource>>(
            [.. Databases.Select(x => new DatabaseResource(x, SubjectUri))]);

    public ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        List<DatabaseDescriptor> descriptors = Databases.Select(x => x.Describe()).ToList();

        return ValueTask.FromResult(new DatabaseUsage
        {
            Cardinality = Cardinality,
            MainDatabase = descriptors.Count == 1 ? descriptors[0] : null,
            Databases = descriptors,
        });
    }

    public async Task AssertEnvironmentAsync(IServiceProvider services, EnvironmentCheckResults results, CancellationToken token)
    {
        foreach (IQuartzWeaselDatabase database in Databases)
        {
            try
            {
                await database.AssertConnectivityAsync(token).ConfigureAwait(false);
                results.RegisterSuccess($"Scheduler '{database.Context.SchedulerName}' can reach {database.Describe().DatabaseUri()}");
            }
            catch (Exception e)
            {
                results.RegisterFailure($"Scheduler '{database.Context.SchedulerName}' cannot reach {database.Describe().DatabaseUri()}", e);
            }
        }
    }

    [RequiresUnreferencedCode("JasperFx's ISystemPart.WriteToConsole says so. This implementation builds its description by hand and reflects over nothing.")]
    public Task WriteToConsole()
    {
        OptionsDescription description = new()
        {
            Subject = typeof(QuartzWeaselSystemPart).FullName!,
            Title = Title,
        };

        OptionSet set = description.AddChildSet("Databases");
        set.Rows.AddRange(Databases.Select(x => x.Describe()));

        OptionDescriptionWriter.Write(description);
        return Task.CompletedTask;
    }

    private IQuartzWeaselDatabase Build(QuartzWeaselRegistration registration)
    {
        string optionsName = registration.SchedulerKey ?? Options.DefaultName;

        // Resolving the store's options is also what runs SingleSchemaOwnerValidator, so a store that
        // asked for ProvisionSchema() as well fails here, before anything reaches the database.
        AdoJobStoreOptions store = services.GetRequiredService<IOptionsMonitor<AdoJobStoreOptions>>().Get(optionsName);

        string schedulerName = registration.SchedulerKey
                               ?? services.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(optionsName).InstanceName;

        QuartzWeaselDialect dialect = registration.Dialect;

        IDbProvider? dbProvider = registration.SchedulerKey is null
            ? services.GetService<IDbProvider>()
            : services.GetKeyedService<IDbProvider>(registration.SchedulerKey);

        if (dbProvider is null)
        {
            throw new SchedulerConfigException(
                $"{dialect.RegistrationMethod}() was called for scheduler '{schedulerName}', but its store has no database"
                + $" for Weasel to manage. Choose the database on the same store, with {dialect.StoreMethod}(...).");
        }

        string connectionString;
        using (DbConnection probe = dbProvider.CreateConnection())
        {
            connectionString = probe.ConnectionString;

            if (!dialect.AcceptsConnection(probe))
            {
                throw new SchedulerConfigException(
                    $"{dialect.RegistrationMethod}() manages a {dialect.DatabaseName} schema, but scheduler '{schedulerName}'"
                    + $" stores its schedule through {probe.GetType().FullName}. Use {dialect.StoreMethod}(...) for the store,"
                    + " or the Quartz.Weasel package for the database it is on.");
            }
        }

        (string? schema, string prefix) = SplitTablePrefix(store.TablePrefix);

        ILogger logger = (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("Quartz.Weasel");

        return registration.CreateDatabase(new QuartzWeaselDatabaseContext
        {
            SchedulerName = schedulerName,
            Schema = schema ?? dialect.DefaultSchema,
            TablePrefix = prefix,
            DbProvider = dbProvider,
            ConnectionString = connectionString,
            MigrationLogger = new QuartzWeaselMigrationLogger(logger, schedulerName),
            Logger = logger,
            SubjectUri = new Uri("quartz://scheduler/" + Uri.EscapeDataString(schedulerName)),
            AutoCreate = registration.AutoCreate,
        });
    }

    /// <summary>
    /// <c>quartz.QRTZ_</c> as schema <c>quartz</c> and prefix <c>QRTZ_</c>, the way the store's own SQL
    /// reads it: everything before the last dot qualifies the table, everything after it prefixes the name.
    /// </summary>
    internal static (string? Schema, string Prefix) SplitTablePrefix(string tablePrefix)
    {
        int lastDot = tablePrefix.LastIndexOf('.');
        return lastDot < 0
            ? (null, tablePrefix)
            : (tablePrefix[..lastDot], tablePrefix[(lastDot + 1)..]);
    }
}
