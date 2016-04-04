#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Common.Logging;

using Quartz.Core;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Impl
{
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
    /// SimpleThreadPool threadPool = new SimpleThreadPool(maxThreads, ThreadPriority.Normal); 
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
	/// <seealso cref="ThreadPool" />
	public class DirectSchedulerFactory : ISchedulerFactory
	{
		private readonly ILog log;
        public const string DefaultInstanceId = "SIMPLE_NON_CLUSTERED";
        public const string DefaultSchedulerName = "SimpleQuartzScheduler";
        private static readonly DefaultThreadExecutor DefaultThreadExecutor = new DefaultThreadExecutor();
        private const int DefaultBatchMaxSize = 1;
        private readonly TimeSpan DefaultBatchTimeWindow = TimeSpan.Zero;

        private bool initialized;
        private static readonly DirectSchedulerFactory instance = new DirectSchedulerFactory();

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
	    public ILog Log
	    {
	        get { return log; }
	    }

	    /// <summary>
		/// Gets the instance.
		/// </summary>
		/// <value>The instance.</value>
		public static DirectSchedulerFactory Instance
		{
			get { return instance; }
		}

		/// <summary> <para>
		/// Returns a handle to all known Schedulers (made by any
		/// StdSchedulerFactory instance.).
		/// </para>
		/// </summary>
		public virtual ICollection<IScheduler> AllSchedulers
		{
			get { return SchedulerRepository.Instance.LookupAll(); }
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectSchedulerFactory"/> class.
        /// </summary>
		protected DirectSchedulerFactory()
		{
		    log = LogManager.GetLogger(GetType());
		}

		/// <summary>
		/// Creates an in memory job store (<see cref="RAMJobStore" />)
		/// The thread priority is set to Thread.NORM_PRIORITY
		/// </summary>
		/// <param name="maxThreads">The number of threads in the thread pool</param>
		public virtual void CreateVolatileScheduler(int maxThreads)
		{
			SimpleThreadPool threadPool = new SimpleThreadPool(maxThreads, ThreadPriority.Normal);
			threadPool.Initialize();
			IJobStore jobStore = new RAMJobStore();
			CreateScheduler(threadPool, jobStore);
		}

		/// <summary>
		/// Creates a proxy to a remote scheduler. This scheduler can be retrieved
		/// via <see cref="DirectSchedulerFactory.GetScheduler()" />.
		/// </summary>
		/// <throws>  SchedulerException </throws>
		public virtual void CreateRemoteScheduler(string proxyAddress)
		{
			CreateRemoteScheduler(DefaultSchedulerName, DefaultInstanceId, proxyAddress);
		}

		/// <summary>
		/// Same as <see cref="DirectSchedulerFactory.CreateRemoteScheduler(string)" />,
		/// with the addition of specifying the scheduler name and instance ID. This
		/// scheduler can only be retrieved via <see cref="DirectSchedulerFactory.GetScheduler(string)" />.
		/// </summary>
		/// <param name="schedulerName">The name for the scheduler.</param>
		/// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
		/// <param name="proxyAddress"></param>
		/// <throws>  SchedulerException </throws>
		protected virtual void CreateRemoteScheduler(string schedulerName, string schedulerInstanceId, string proxyAddress)
		{
			string uid = QuartzSchedulerResources.GetUniqueIdentifier(schedulerName, schedulerInstanceId);

		    var proxyBuilder = new RemotingSchedulerProxyFactory();
		    proxyBuilder.Address = proxyAddress;
		    RemoteScheduler remoteScheduler = new RemoteScheduler(uid, proxyBuilder);
			
            SchedulerRepository schedRep = SchedulerRepository.Instance;
			schedRep.Bind(remoteScheduler);
		    initialized = true;
		}

		/// <summary> 
		/// Creates a scheduler using the specified thread pool and job store. This
		/// scheduler can be retrieved via DirectSchedulerFactory#GetScheduler()
		/// </summary>
		/// <param name="threadPool">
		/// The thread pool for executing jobs
		/// </param>
		/// <param name="jobStore">
		/// The type of job store
		/// </param>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if initialization failed
		/// </summary>
		public virtual void CreateScheduler(IThreadPool threadPool, IJobStore jobStore)
		{
			CreateScheduler(DefaultSchedulerName, DefaultInstanceId, threadPool, jobStore);
			initialized = true;
		}

		/// <summary>
		/// Same as DirectSchedulerFactory#createScheduler(ThreadPool threadPool, JobStore jobStore),
		/// with the addition of specifying the scheduler name and instance ID. This
		/// scheduler can only be retrieved via DirectSchedulerFactory#getScheduler(String)
		/// </summary>
		/// <param name="schedulerName">The name for the scheduler.</param>
		/// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
		/// <param name="threadPool">The thread pool for executing jobs</param>
		/// <param name="jobStore">The type of job store</param>
		public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool,
		                                    IJobStore jobStore)
		{
			CreateScheduler(schedulerName, schedulerInstanceId, threadPool, jobStore, TimeSpan.Zero);
		}

	    /// <summary>
	    /// Creates a scheduler using the specified thread pool and job store and
	    /// binds it for remote access.
	    /// </summary>
	    /// <param name="schedulerName">The name for the scheduler.</param>
	    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
	    /// <param name="threadPool">The thread pool for executing jobs</param>
	    /// <param name="jobStore">The type of job store</param>
	    /// <param name="idleWaitTime">The idle wait time. You can specify "-1" for
	    /// the default value, which is currently 30000 ms.</param>
	    public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool,
                                            IJobStore jobStore, TimeSpan idleWaitTime)
        {
            CreateScheduler(schedulerName, schedulerInstanceId, threadPool, jobStore, null, idleWaitTime);
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
	    /// <param name="idleWaitTime">The idle wait time. You can specify TimeSpan.Zero for
	    /// the default value, which is currently 30000 ms.</param>
	    public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool,
                                            IJobStore jobStore, IDictionary<string, ISchedulerPlugin> schedulerPluginMap, TimeSpan idleWaitTime)
		{
			CreateScheduler(
                schedulerName, schedulerInstanceId, threadPool, DefaultThreadExecutor, 
                jobStore, schedulerPluginMap, idleWaitTime);
		}

	    /// <summary>
	    /// Creates a scheduler using the specified thread pool and job store and
	    /// binds it for remote access.
	    /// </summary>
	    /// <param name="schedulerName">The name for the scheduler.</param>
	    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
	    /// <param name="threadPool">The thread pool for executing jobs</param>
	    /// <param name="threadExecutor">Thread executor.</param>
	    /// <param name="jobStore">The type of job store</param>
	    /// <param name="schedulerPluginMap"></param>
	    /// <param name="idleWaitTime">The idle wait time. You can specify TimeSpan.Zero for
	    /// the default value, which is currently 30000 ms.</param>
	    public virtual void CreateScheduler(string schedulerName, string schedulerInstanceId, IThreadPool threadPool, IThreadExecutor threadExecutor,
                                            IJobStore jobStore, IDictionary<string, ISchedulerPlugin> schedulerPluginMap, TimeSpan idleWaitTime)
        {
            CreateScheduler(schedulerName, schedulerInstanceId, threadPool, threadExecutor, jobStore, schedulerPluginMap, idleWaitTime, DefaultBatchMaxSize, DefaultBatchTimeWindow);
           
        }

	    /// <summary>
	    /// Creates a scheduler using the specified thread pool and job store and
	    /// binds it for remote access.
	    /// </summary>
	    /// <param name="schedulerName">The name for the scheduler.</param>
	    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
	    /// <param name="threadPool">The thread pool for executing jobs</param>
	    /// <param name="threadExecutor">Thread executor.</param>
	    /// <param name="jobStore">The type of job store</param>
	    /// <param name="schedulerPluginMap"></param>
	    /// <param name="idleWaitTime">The idle wait time. You can specify TimeSpan.Zero for
	    ///     the default value, which is currently 30000 ms.</param>
	    /// <param name="maxBatchSize">The maximum batch size of triggers, when acquiring them</param>
	    /// <param name="batchTimeWindow">The time window for which it is allowed to "pre-acquire" triggers to fire</param>
	    public virtual void CreateScheduler(
	        string schedulerName,
	        string schedulerInstanceId,
	        IThreadPool threadPool,
	        IThreadExecutor threadExecutor,
	        IJobStore jobStore,
	        IDictionary<string, ISchedulerPlugin> schedulerPluginMap,
	        TimeSpan idleWaitTime,
	        int maxBatchSize,
	        TimeSpan batchTimeWindow)
	    {
	        CreateScheduler(schedulerName, schedulerInstanceId, threadPool, threadExecutor, jobStore, schedulerPluginMap, idleWaitTime, maxBatchSize, batchTimeWindow, null);
	    }

	    /// <summary>
	    /// Creates a scheduler using the specified thread pool and job store and
	    /// binds it for remote access.
	    /// </summary>
	    /// <param name="schedulerName">The name for the scheduler.</param>
	    /// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
	    /// <param name="threadPool">The thread pool for executing jobs</param>
	    /// <param name="threadExecutor">Thread executor.</param>
	    /// <param name="jobStore">The type of job store</param>
	    /// <param name="schedulerPluginMap"></param>
	    /// <param name="idleWaitTime">The idle wait time. You can specify TimeSpan.Zero for
	    ///     the default value, which is currently 30000 ms.</param>
	    /// <param name="maxBatchSize">The maximum batch size of triggers, when acquiring them</param>
	    /// <param name="batchTimeWindow">The time window for which it is allowed to "pre-acquire" triggers to fire</param>
	    /// <param name="schedulerExporter">The scheduler exporter to use</param>
	    public virtual void CreateScheduler(
            string schedulerName, 
            string schedulerInstanceId, 
            IThreadPool threadPool, 
            IThreadExecutor threadExecutor, 
            IJobStore jobStore, 
            IDictionary<string, ISchedulerPlugin> schedulerPluginMap, 
            TimeSpan idleWaitTime, 
            int maxBatchSize, 
            TimeSpan batchTimeWindow, 
            ISchedulerExporter schedulerExporter)
        {
            // Currently only one run-shell factory is available...
            IJobRunShellFactory jrsf = new StdJobRunShellFactory();

            // Fire everything up
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            SchedulerDetailsSetter.SetDetails(threadPool, schedulerName, schedulerInstanceId);
           
            threadPool.Initialize();

            QuartzSchedulerResources qrs = new QuartzSchedulerResources();

            qrs.Name = schedulerName;
            qrs.InstanceId = schedulerInstanceId;

            qrs.JobRunShellFactory = jrsf;
            qrs.ThreadPool = threadPool;
            qrs.ThreadExecutor= threadExecutor;
            qrs.JobStore = jobStore;
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

            QuartzScheduler qs = new QuartzScheduler(qrs, idleWaitTime);

            ITypeLoadHelper cch = new SimpleTypeLoadHelper();
            cch.Initialize();

            SchedulerDetailsSetter.SetDetails(jobStore, schedulerName, schedulerInstanceId);
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

            Log.Info(string.Format(CultureInfo.InvariantCulture, "Quartz scheduler '{0}", scheduler.SchedulerName));

            Log.Info(string.Format(CultureInfo.InvariantCulture, "Quartz scheduler version: {0}", qs.Version));

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
		public virtual IScheduler GetScheduler()
		{
			if (!initialized)
			{
				throw new SchedulerException(
					"you must call createRemoteScheduler or createScheduler methods before calling getScheduler()");
			}
			SchedulerRepository schedRep = SchedulerRepository.Instance;

			return schedRep.Lookup(DefaultSchedulerName);
		}

		/// <summary>
		/// Returns a handle to the Scheduler with the given name, if it exists.
		/// </summary>
		public virtual IScheduler GetScheduler(string schedName)
		{
			SchedulerRepository schedRep = SchedulerRepository.Instance;

			return schedRep.Lookup(schedName);
		}
	}
}
