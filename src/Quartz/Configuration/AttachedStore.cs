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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// One database this process has been pointed at, and the windows onto the schedulers found in it.
/// </summary>
/// <remarks>
/// <para>
/// The store is the whole of the reach: nothing is asked of the processes that run these schedulers,
/// no port is opened towards them, and they are not changed in any way. What is read is what they
/// wrote — their jobs, their triggers, their fired-trigger rows, their check-ins, and their execution
/// history when they keep it here.
/// </para>
/// <para>
/// Two things are built from the one recipe the caller gave. A <em>probe</em> — a container holding
/// the connection provider and the driver delegate, and no scheduler at all — which is what discovery
/// runs through and what a window's history is read with, because neither question belongs to any one
/// scheduler name. And one <em>window</em> per discovered name: a real scheduler over the same store,
/// built through <see cref="ISchedulerRuntime.Add" /> and never started, whose thread pool creates no
/// threads.
/// </para>
/// <para>
/// A name that this process already has a scheduler under is refused, naming both — the window would
/// be a second scheduler wearing one name, and every page that resolves a scheduler by name would
/// reach whichever was bound first. Refusing one window does not stop the others: an attached store
/// holding one colliding name is still worth every other name in it.
/// </para>
/// </remarks>
internal sealed class AttachedStore : IAsyncDisposable
{
    /// <summary>
    /// The instance id a window's store carries.
    /// </summary>
    /// <remarks>
    /// A window writes no check-in row and no fired-trigger row, so this never reaches the database.
    /// It is set all the same, because the default for a non-clustered store is
    /// <c>NON_CLUSTERED</c> — the id a leader presents — and a window is emphatically not one. The
    /// node listing excludes an observer whatever its id, so this cannot be mistaken for a worker
    /// even if a worker happens to carry the same string.
    /// </remarks>
    internal const string WindowInstanceId = "WINDOW";

    /// <summary>
    /// The scheduler name the probe's own container is registered under.
    /// </summary>
    /// <remarks>
    /// Nothing is ever built under it — the probe holds a connection provider and a driver delegate and
    /// no scheduler — but a container has to name the scheduler whose parts it is registering, and the
    /// name reaches the database in nothing the probe issues: discovery names no scheduler, and the
    /// history statements take theirs from the query.
    /// </remarks>
    private const string ProbeSchedulerName = "quartz-attached-store-probe";

    private readonly IServiceProvider application;
    private readonly Action<IPersistentStoreBuilder> configure;
    private readonly ILogger<AttachedStore> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// The scheduler names this target has already decided about, whether a window was opened or the
    /// name was refused. Read and written under <see cref="gate" />.
    /// </summary>
    private readonly Dictionary<string, byte> windows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The scheduler names in this database that could not become windows, and why.
    /// </summary>
    /// <remarks>
    /// A name that collides with a scheduler of this process is the case this exists for. It is logged
    /// as well — a scheduler an operator cannot find must not be silently absent — and kept here so the
    /// refusal can be read rather than only tailed.
    /// </remarks>
    private readonly Dictionary<string, string> refusals = new(StringComparer.OrdinalIgnoreCase);

    private ServiceProvider? probe;
    private IDbProvider? dbProvider;
    private IDriverDelegate? driverDelegate;
    private bool disposed;

    /// <param name="target">The name this store was attached under, which names its windows.</param>
    /// <param name="configure">
    /// The store recipe, which is the cluster's own: the dialect, the table prefix, the serializer and
    /// any custom trigger serializers. A window that read the blobs with a different configuration
    /// would fail on the first trigger it did not recognise.
    /// </param>
    /// <param name="application">The container the windows are added to and read from.</param>
    public AttachedStore(string target, Action<IPersistentStoreBuilder> configure, IServiceProvider application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(application);

        Target = target;
        this.configure = configure;
        this.application = application;
        logger = application.GetService<ILoggerFactory>() is { } factory
            ? factory.CreateLogger<AttachedStore>()
            : LogProvider.CreateLogger<AttachedStore>();
    }

    /// <summary>
    /// The name this store was attached under. Half of a window's identity, and the half that says
    /// which database a scheduler name was found in.
    /// </summary>
    public string Target { get; }

    /// <inheritdoc cref="refusals" />
    public IReadOnlyDictionary<string, string> Refusals => refusals;

    /// <summary>
    /// The history this store keeps, or <see langword="null" /> when the recipe did not ask for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One store for every window of this target rather than one each: the two history tables are keyed
    /// by scheduler name and every query carries one, so a second instance would only be a second
    /// connection doing the same work.
    /// </para>
    /// <para>
    /// It never writes, so it never sweeps: retention is the cluster's own nodes' business, and a
    /// dashboard deleting a cluster's history because its own window was shorter would be a surprise
    /// nobody asked for. Reads still apply the age bound, as they do everywhere.
    /// </para>
    /// </remarks>
    public IExecutionHistoryStore? History { get; private set; }

    /// <summary>
    /// Every scheduler name this database holds anything under.
    /// </summary>
    /// <remarks>
    /// Sorted ordinally, as the paged queries over a job store are, so that two runs of the same
    /// discovery produce the same listing.
    /// </remarks>
    public async ValueTask<List<string>> Discover(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        Probe();

        DbConnection connection = dbProvider!.CreateConnection();
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Owning nothing: the connection is disposed above and there is no transaction to commit.
            // Discovery is a read of three tables and must not take the trigger lock — a dashboard
            // looking for scheduler names has no business making a cluster wait.
            ConnectionAndTransactionHolder holder = new(connection, transaction: null, ownsResources: false);
            List<string> names = await driverDelegate!.SelectSchedulerNames(holder, cancellationToken).ConfigureAwait(false);

            names.Sort(static (left, right) => string.CompareOrdinal(left, right));
            return names;
        }
    }

    /// <summary>
    /// Discovers the scheduler names in this database and builds a window for each one this process
    /// does not have yet.
    /// </summary>
    /// <remarks>
    /// Safe to call again: a name that already has a window is skipped, so the timer costs one query
    /// and nothing else once a database has settled. Nothing is ever removed — a scheduler whose rows
    /// were deleted is a window with an empty schedule, which is a truthful page, and removing it under
    /// an operator who is reading it is not.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The names windows were built for by this call.</returns>
    public async ValueTask<List<string>> Synchronize(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<string> discovered = await Discover(cancellationToken).ConfigureAwait(false);
            List<string> added = [];

            ISchedulerRuntime runtime = application.GetRequiredService<ISchedulerRuntime>();
            SchedulerWindowRegistry registry = application.GetRequiredService<SchedulerWindowRegistry>();

            foreach (string schedulerName in discovered)
            {
                if (windows.ContainsKey(schedulerName))
                {
                    continue;
                }

                try
                {
                    await runtime.Add(schedulerName, Recipe, SchedulerAddOptions.WithoutStarting, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SchedulerConfigException collision)
                {
                    // One name that cannot be a window does not cost the operator the rest of the
                    // database. Recorded as decided, so the next round does not try again and log again,
                    // and the reason is kept so the refusal can be read rather than only tailed.
                    string problem =
                        $"'{Target}/{schedulerName}' cannot be shown: this process already has a scheduler named "
                        + $"'{schedulerName}', and two schedulers under one name is a name that resolves to "
                        + $"whichever was bound first. {collision.Message}";

                    windows[schedulerName] = 0;
                    refusals[schedulerName] = problem;
                    logger.AttachedStoreWindowRefused(Target, schedulerName, problem, collision);
                    continue;
                }

                windows[schedulerName] = 0;
                registry.Add(schedulerName, Target);
                added.Add(schedulerName);
                logger.AttachedStoreWindowOpened(Target, schedulerName);
            }

            return added;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// What a window is: the cluster's store, no threads, and an observer's reading of the check-ins.
    /// </summary>
    private void Recipe(IQuartzBuilder quartz)
    {
        quartz.ConfigureScheduler(static options => options.InstanceId = WindowInstanceId);

        // No threads at all, and the two members a running scheduler calls throw — so a window that
        // something tried to start would say so rather than quietly acquiring triggers a cluster node
        // should have had.
        quartz.UseThreadPool<ZeroSizeThreadPool>();

        quartz.UsePersistentStore(store =>
        {
            configure(store);

            // Last, so it cannot be turned off by the recipe: a window that counted itself among the
            // nodes would report a cluster as running because a dashboard is watching it.
            store.ConfigureStore(static options => options.ClusterObserver = true);
        });
    }

    /// <summary>
    /// Builds the probe container on first use: the connection provider, the driver delegate, and the
    /// history store when the recipe asked for one.
    /// </summary>
    /// <remarks>
    /// The delegate is initialized here rather than by a job store, because there is no job store: the
    /// two questions the probe answers — which schedulers are in this database, and what did they run —
    /// belong to the database rather than to any one scheduler name.
    /// </remarks>
    private void Probe()
    {
        if (probe is not null)
        {
            return;
        }

        ServiceCollection services = [];

        // Where a log line goes is the application's decision, and discovery's lines should land where
        // the rest of Quartz's do. The clock likewise: a test that moves the application's forward must
        // move the history's retention floor with it.
        if (application.GetService<ILoggerFactory>() is { } loggerFactory)
        {
            services.AddSingleton(loggerFactory);
        }

        if (application.GetService<TimeProvider>() is { } timeProvider)
        {
            services.AddSingleton(timeProvider);
        }

        new QuartzBuilder(services, schedulerKey: null)
            .ConfigureScheduler(static options => options.InstanceName = ProbeSchedulerName)
            .UsePersistentStore(configure);

        services.AddQuartzScheduler();

        ServiceProvider built = services.BuildServiceProvider();
        try
        {
            AdoJobStoreOptions storeOptions = built.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;
            IDbProvider provider = built.GetRequiredService<IDbProvider>();
            IDriverDelegate driver = built.GetRequiredService<IDriverDelegate>();

            driver.Initialize(new DriverDelegateContext
            {
                UseProperties = storeOptions.StoreJobDataAsStrings,
                TablePrefix = storeOptions.TablePrefix ?? "",
                SchedulerName = ProbeSchedulerName,
                InstanceId = WindowInstanceId,
                DbProvider = provider,
                TypeLoader = built.GetRequiredService<ITypeLoader>(),
                ObjectSerializer = built.GetService<IObjectSerializer>(),
                TriggerPersistenceDelegates = built.GetServices<ITriggerPersistenceDelegate>().ToArray(),
                TimeProvider = built.GetRequiredService<TimeProvider>(),
                CommandTimeout = storeOptions.CommandTimeout,
                LoggerFactory = built.GetRequiredService<ILoggerFactory>(),
            });

            if (storeOptions.ExecutionHistory)
            {
                // Built over the probe's provider and delegate, and over the application's history
                // bounds: what a dashboard shows of a cluster's history is the dashboard's window on it,
                // and a second set of bounds nobody configured would be a page whose age limit nothing
                // explained.
                History = new AdoExecutionHistoryStore(
                    provider,
                    driver,
                    application.GetService<IOptions<ExecutionHistoryOptions>>() ?? Options.Create(new ExecutionHistoryOptions()),
                    Options.Create(new QuartzSchedulerOptions { InstanceName = ProbeSchedulerName }),
                    built.GetRequiredService<TimeProvider>(),
                    built.GetRequiredService<ILoggerFactory>());
            }

            dbProvider = provider;
            driverDelegate = driver;
            probe = built;
        }
        catch (OptionsValidationException failure)
        {
            built.Dispose();

            // The same translation everything else that builds a scheduler from a recipe makes: options
            // validation is how configuration is checked, and its exception is an implementation detail
            // of that rather than something an application attaching a store should have to catch.
            throw new SchedulerConfigException(
                $"The store attached as '{Target}' is not configured correctly: {string.Join(" ", failure.Failures)}",
                failure);
        }
        catch
        {
            built.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // The windows are the runtime's and go down with it or with the host; this owns the probe, and
        // the probe owns a connection provider of its own.
        (History as IDisposable)?.Dispose();

        if (probe is not null)
        {
            await probe.DisposeAsync().ConfigureAwait(false);
            probe = null;
        }

        gate.Dispose();
    }
}
