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

using Microsoft.Extensions.Logging;

using Quartz.Core;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Impl;

/// <summary>
/// A singleton implementation of <see cref="ISchedulerFactory" />.
/// </summary>
/// <remarks>
/// Here are some examples of using this class:
/// <para>
/// To create a scheduler that does not write anything to the database (is not
/// persistent), you can call <see cref="CreateVolatileScheduler" />:
/// </para>
/// <code>
/// DirectSchedulerFactory.Instance.CreateVolatileScheduler(10); // 10 threads
/// // don't forget to start the scheduler:
/// DirectSchedulerFactory.Instance.GetScheduler().Start();
/// </code>
/// <para>
/// Several create methods are provided for convenience. All create methods
/// eventually end up calling the create method with all the parameters:
/// </para>
/// <code>
/// public void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool, IJobStore jobStore)
/// </code>
/// <para>
/// Here is an example of using this method:
/// </para>
/// <code>
/// // create the thread pool
/// var threadPool = new DefaultThreadPool();
/// threadPool.MaxConcurrency = maxConcurrency;
/// threadPool.Initialize();
/// // create the job store
/// JobStore jobStore = new RAMJobStore();
///
/// DirectSchedulerFactory.Instance.CreateScheduler("My Quartz Scheduler", "My Instance", threadPool, jobStore);
/// // don't forget to start the scheduler:
/// DirectSchedulerFactory.Instance.GetScheduler("My Quartz Scheduler", "My Instance").Start();
/// </code>
/// </remarks>>
/// <author>Mohammad Rezaei</author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
/// <seealso cref="IJobStore" />
public class DirectSchedulerFactory : ISchedulerFactory
{
    public const string DefaultInstanceId = "SIMPLE_NON_CLUSTERED";
    public const string DefaultSchedulerName = "SimpleQuartzScheduler";

    private bool initialized;

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    private ILogger<DirectSchedulerFactory> logger { get; }

    /// <summary>
    /// Gets the instance.
    /// </summary>
    /// <value>The instance.</value>
    public static DirectSchedulerFactory Instance { get; } = new DirectSchedulerFactory();

    /// <summary> <para>
    /// Returns a handle to all known Schedulers (made by any
    /// StdSchedulerFactory instance.).
    /// </para>
    /// </summary>
    public virtual ValueTask<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
    {
        return SchedulerRepository.Instance.LookupAll(cancellationToken);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectSchedulerFactory"/> class.
    /// </summary>
    protected DirectSchedulerFactory()
    {
        logger = LogProvider.CreateLogger<DirectSchedulerFactory>();
    }

    /// <summary>
    /// Creates an in memory job store (<see cref="RAMJobStore" />)
    /// </summary>
    /// <param name="maxConcurrency">The number of allowed concurrent running tasks.</param>
    public virtual void CreateVolatileScheduler(int maxConcurrency)
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = maxConcurrency
        };
        threadPool.Initialize();
        IJobStore jobStore = new RAMJobStore();
        CreateScheduler(threadPool, jobStore);
    }

#if REMOTING
    /// <summary>
    /// Creates a proxy to a remote scheduler. This scheduler can be retrieved
    /// via <see cref="DirectSchedulerFactory.GetScheduler(CancellationToken)" />.
    /// </summary>
    /// <throws>  SchedulerException </throws>
    public virtual void CreateRemoteScheduler(string proxyAddress)
    {
        CreateRemoteScheduler(DefaultSchedulerName, DefaultInstanceId, proxyAddress);
    }

    /// <summary>
    /// Same as <see cref="DirectSchedulerFactory.CreateRemoteScheduler(string)" />,
    /// with the addition of specifying the scheduler name and instance ID. This
    /// scheduler can only be retrieved via <see cref="DirectSchedulerFactory.GetScheduler(string, CancellationToken)" />.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="proxyAddress"></param>
    /// <throws>  SchedulerException </throws>
    protected virtual void CreateRemoteScheduler(string schedulerName, string schedulerInstanceId, string proxyAddress)
    {
        var proxyBuilder = new RemotingSchedulerProxyFactory();
        proxyBuilder.Address = proxyAddress;
        IScheduler remoteScheduler = proxyBuilder.GetProxy(schedulerName, schedulerInstanceId);

        SchedulerRepository schedRep = SchedulerRepository.Instance;
        schedRep.Bind(remoteScheduler);
        initialized = true;
    }
#endif // REMOTING

    /// <summary>
    /// Creates a scheduler using the specified thread pool and job store, and with an idle wait time of
    /// <c>30</c> seconds. This scheduler can be retrieved via <see cref="GetScheduler(CancellationToken)"/>.
    /// </summary>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <exception cref="SchedulerException">Initialization failed.</exception>
    public virtual void CreateScheduler(IThreadPool threadPool, IJobStore jobStore)
    {
        CreateScheduler(DefaultSchedulerName, DefaultInstanceId, threadPool, jobStore);
    }

    /// <summary>
    /// Same as <see cref="DirectSchedulerFactory.CreateScheduler(IThreadPool, IJobStore)" />,
    /// with the addition of specifying the scheduler name and instance ID. This
    /// scheduler can only be retrieved via <see cref="GetScheduler(string, CancellationToken)"/>.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <exception cref="SchedulerException">Initialization failed.</exception>
    public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool,
        IJobStore jobStore)
    {
        CreateScheduler(schedulerName, schedulerInstanceId, threadPool, jobStore, QuartzSchedulerResources.DefaultIdleWaitTime);
    }

    /// <summary>
    /// Creates a scheduler using the specified thread pool and job store and
    /// binds it for remote access.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <param name="idleWaitTime">The idle wait time.</param>
    /// <exception cref="SchedulerException">Initialization failed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="idleWaitTime"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool,
        IJobStore jobStore, TimeSpan idleWaitTime)
    {
        CreateScheduler(schedulerName, schedulerInstanceId, threadPool, jobStore, null, idleWaitTime);
    }

    /// <summary>
    /// Creates a scheduler using the specified thread pool and job store, and
    /// binds it for remote access.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <param name="schedulerPluginMap"></param>
    /// <param name="idleWaitTime">The idle wait time.</param>
    /// <exception cref="SchedulerException">Initialization failed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="idleWaitTime"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public virtual void CreateScheduler(
        string schedulerName,
        string schedulerInstanceId,
        IThreadPool threadPool,
        IJobStore jobStore,
        IDictionary<string, ISchedulerPlugin>? schedulerPluginMap,
        TimeSpan idleWaitTime)
    {
        CreateScheduler(
            schedulerName,
            schedulerInstanceId,
            threadPool,
            jobStore,
            schedulerPluginMap,
            idleWaitTime,
            QuartzSchedulerResources.DefaultMaxBatchSize,
            QuartzSchedulerResources.DefaultBatchTimeWindow);
    }

    /// <summary>
    /// Creates a scheduler using the specified thread pool and job store and
    /// binds it for remote access.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <param name="schedulerPluginMap"></param>
    /// <param name="idleWaitTime">The idle wait time.</param>
    /// <param name="maxBatchSize">The maximum batch size of triggers, when acquiring them</param>
    /// <param name="batchTimeWindow">The time window for which it is allowed to "pre-acquire" triggers to fire</param>
    /// <exception cref="SchedulerException">Initialization failed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="idleWaitTime"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBatchSize"/> is less than <c>1</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchTimeWindow"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public virtual void CreateScheduler(
        string schedulerName,
        string schedulerInstanceId,
        IThreadPool threadPool,
        IJobStore jobStore,
        IDictionary<string, ISchedulerPlugin>? schedulerPluginMap,
        TimeSpan idleWaitTime,
        int maxBatchSize,
        TimeSpan batchTimeWindow)
    {
        CreateScheduler(
            schedulerName,
            schedulerInstanceId,
            threadPool,
            jobStore,
            schedulerPluginMap,
            idleWaitTime,
            maxBatchSize,
            batchTimeWindow,
            null);
    }

    /// <summary>
    /// Creates a scheduler using the specified thread pool and job store and
    /// binds it for remote access.
    /// </summary>
    /// <param name="schedulerName">The name for the scheduler.</param>
    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
    /// <param name="threadPool">The thread pool for executing jobs</param>
    /// <param name="jobStore">The type of job store</param>
    /// <param name="schedulerPluginMap"></param>
    /// <param name="idleWaitTime">The idle wait time.</param>
    /// <param name="maxBatchSize">The maximum batch size of triggers, when acquiring them</param>
    /// <param name="batchTimeWindow">The time window for which it is allowed to "pre-acquire" triggers to fire</param>
    /// <param name="schedulerExporter">The scheduler exporter to use</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="idleWaitTime"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBatchSize"/> is less than <c>1</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchTimeWindow"/> is less than <see cref="TimeSpan.Zero"/>.</exception>
    public virtual void CreateScheduler(
        string schedulerName,
        string schedulerInstanceId,
        IThreadPool threadPool,
        IJobStore jobStore,
        IDictionary<string, ISchedulerPlugin>? schedulerPluginMap,
        TimeSpan idleWaitTime,
        int maxBatchSize,
        TimeSpan batchTimeWindow,
        ISchedulerExporter? schedulerExporter)
    {
        if (idleWaitTime < TimeSpan.Zero)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(idleWaitTime), $"Cannot be less than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}.");
        }

        if (maxBatchSize < 1)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(maxBatchSize), "Cannot be less than 1.");
        }

        if (batchTimeWindow < TimeSpan.Zero)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(batchTimeWindow), $"Cannot be less than {nameof(TimeSpan)}.{nameof(TimeSpan.Zero)}.");
        }

        // Currently only one run-shell factory is available...
        IJobRunShellFactory jrsf = new StdJobRunShellFactory();

        // Fire everything up
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        threadPool.InstanceName = schedulerName;
        threadPool.InstanceId = schedulerInstanceId;

        threadPool.Initialize();

        QuartzSchedulerResources qrs = new QuartzSchedulerResources();

        qrs.Name = schedulerName;
        qrs.InstanceId = schedulerInstanceId;
        qrs.JobRunShellFactory = jrsf;
        qrs.ThreadPool = threadPool;
        qrs.JobStore = jobStore;
        qrs.IdleWaitTime = idleWaitTime;
        qrs.MaxBatchSize = maxBatchSize;
        qrs.BatchTimeWindow = batchTimeWindow;
        qrs.SchedulerExporter = schedulerExporter;

        // add plugins
        if (schedulerPluginMap != null)
        {
            foreach (ISchedulerPlugin plugin in schedulerPluginMap.Values)
            {
                qrs.AddSchedulerPlugin(plugin);
            }
        }

        QuartzScheduler qs = new QuartzScheduler(qrs);

        ITypeLoadHelper cch = new SimpleTypeLoadHelper();
        cch.Initialize();

        jobStore.InstanceName = schedulerName;
        jobStore.InstanceId = schedulerInstanceId;
        jobStore.Initialize(cch, qs.SchedulerSignaler);

        IScheduler scheduler = new StdScheduler(qs);

        jrsf.Initialize(scheduler);

        qs.Initialize();

        // Initialize plugins now that we have a Scheduler instance.
        if (schedulerPluginMap != null)
        {
            foreach (var pluginEntry in schedulerPluginMap)
            {
                pluginEntry.Value.Initialize(pluginEntry.Key, scheduler);
            }
        }

        logger.LogInformation("Quartz scheduler {SchedulerName}", scheduler.SchedulerName);

        logger.LogInformation("Quartz scheduler version: {Version}", qs.Version);

        SchedulerRepository schedRep = SchedulerRepository.Instance;

        qs.AddNoGCObject(schedRep); // prevents the repository from being
        // garbage collected

        schedRep.Bind(scheduler);

        initialized = true;
    }

    /// <summary>
    /// Returns a handle to the Scheduler produced by this factory.
    /// <para>
    /// you must call createRemoteScheduler or createScheduler methods before
    /// calling getScheduler()
    /// </para>
    /// </summary>
    /// <returns></returns>
    /// <throws>  SchedulerException </throws>
    public virtual ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        if (!initialized)
        {
            ThrowHelper.ThrowSchedulerException(
                "you must call createRemoteScheduler or createScheduler methods before calling getScheduler()");
        }
        SchedulerRepository schedRep = SchedulerRepository.Instance;

        return schedRep.Lookup(DefaultSchedulerName, cancellationToken)!;
    }

    /// <summary>
    /// Returns a handle to the Scheduler with the given name, if it exists.
    /// </summary>
    public virtual ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        SchedulerRepository schedRep = SchedulerRepository.Instance;
        return schedRep.Lookup(schedName, cancellationToken);
    }
}