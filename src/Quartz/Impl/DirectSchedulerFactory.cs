/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
using System.Threading;
using log4net;
using Quartz.Core;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Impl
{
	/// <summary>
	/// A singleton implementation of <code>SchedulerFactory</code>.
	/// <p>
	/// Here are some examples of using this class:
	/// </p>
	/// <p>
	/// To create a scheduler that does not write anything to the database (is not
	/// persistent), you can call <code>createVolatileScheduler</code>:
	/// </p>
	/// <pre>
	/// DirectSchedulerFactory.getInstance().createVolatileScheduler(10); // 10 threads 
	/// // don't forget to start the scheduler: 
	/// DirectSchedulerFactory.getInstance().getScheduler().start();
	/// </pre>
	/// <p>
	/// Several create methods are provided for convenience. All create methods
	/// eventually end up calling the create method with all the parameters:
	/// </p>
	/// <pre>
	/// public void createScheduler(String schedulerName, string schedulerInstanceId, ThreadPool threadPool, JobStore jobStore, string rmiRegistryHost, int rmiRegistryPort)
	/// </pre>
	/// <p>
	/// Here is an example of using this method:
	/// </p>
	/// <pre>
	/// // create the thread pool 
	/// SimpleThreadPool threadPool = new SimpleThreadPool(maxThreads, Thread.NORM_PRIORITY); 
	/// threadPool.initialize(); 
	/// // create the job store 
	/// JobStore jobStore = new RAMJobStore(); 
	/// jobStore.initialize();
	/// 
	/// DirectSchedulerFactory.getInstance().createScheduler("My Quartz Scheduler", "My Instance", threadPool, jobStore, "localhost", 1099); 
	/// // don't forget to start the scheduler: 
	/// DirectSchedulerFactory.getInstance().getScheduler("My Quartz Scheduler", "My Instance").start();
	/// </pre>
	/// <p>
	/// You can also use a JDBCJobStore instead of the RAMJobStore:
	/// </p>
	/// <pre>
	/// DBConnectionManager.getInstance().addConnectionProvider("someDatasource", new JNDIConnectionProvider("someDatasourceJNDIName"));
	/// 
	/// JDBCJobStore jdbcJobStore = new JDBCJobStore(); jdbcJobStore.setDataSource("someDatasource"); 
	/// jdbcJobStore.setPostgresStyleBlobs(true); 
	/// jdbcJobStore.setTablePrefix("QRTZ_"); 
	/// jdbcJobStore.setInstanceId("My Instance"); 
	/// jdbcJobStore.initialize();
	/// </pre>
	/// </summary>
	/// <author>Mohammad Rezaei</author>
	/// <author>James House</author>
	/// <seealso cref="IJobStore" />
	/// <seealso cref="ThreadPool" />
	public class DirectSchedulerFactory : ISchedulerFactory
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (DirectSchedulerFactory));


		public static DirectSchedulerFactory Instance
		{
			get { return instance; }
		}

		/// <summary> <p>
		/// Returns a handle to all known Schedulers (made by any
		/// StdSchedulerFactory instance.).
		/// </p>
		/// </summary>
		public virtual ICollection AllSchedulers
		{
			get { return SchedulerRepository.Instance.lookupAll(); }
		}

		public const string DEFAULT_INSTANCE_ID = "SIMPLE_NON_CLUSTERED";
		public const string DEFAULT_SCHEDULER_NAME = "SimpleQuartzScheduler";

		private bool initialized = false;
		private static DirectSchedulerFactory instance = new DirectSchedulerFactory();

		/// <summary>
		/// Constructor
		/// </summary>
		protected internal DirectSchedulerFactory()
		{
		}

		/// <summary>
		/// Creates an in memory job store (<code>RAMJobStore</code>)
		/// The thread priority is set to Thread.NORM_PRIORITY
		/// </summary>
		/// <param name="maxThreads">The number of threads in the thread pool</param>
		public virtual void CreateVolatileScheduler(int maxThreads)
		{
			SimpleThreadPool threadPool = new SimpleThreadPool(maxThreads, (int) ThreadPriority.Normal);
			threadPool.Initialize();
			IJobStore jobStore = new RAMJobStore();
			CreateScheduler(threadPool, jobStore);
		}

		/// <summary>
		/// Creates a proxy to a remote scheduler. This scheduler can be retrieved
		/// via DirectSchedulerFactory#GetScheduler()}
		/// </summary>
		/// <param name="rmiHost">The hostname for remote scheduler</param>
		/// <param name="rmiPort">Port for the remote scheduler. The default RMI port is 1099.</param>
		/// <throws>  SchedulerException </throws>
		public virtual void CreateRemoteScheduler(string rmiHost, int rmiPort)
		{
			CreateRemoteScheduler(DEFAULT_SCHEDULER_NAME, DEFAULT_INSTANCE_ID, rmiHost, rmiPort);
			initialized = true;
		}

		/// <summary>
		/// Same as
		/// DirectSchedulerFactory#createRemoteScheduler(string rmiHost, int rmiPort),
		/// with the addition of specifying the scheduler name and instance ID. This
		/// scheduler can only be retrieved via
		///DirectSchedulerFactory#getScheduler(string)
		/// </summary>
		/// <param name="schedulerName">The name for the scheduler.</param>
		/// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
		/// <param name="rmiHost">The hostname for remote scheduler</param>
		/// <param name="rmiPort">Port for the remote scheduler. The default RMI port is 1099.</param>
		/// <throws>  SchedulerException </throws>
		protected internal virtual void CreateRemoteScheduler(String schedulerName, string schedulerInstanceId, string rmiHost,
		                                                      int rmiPort)
		{
			SchedulingContext schedCtxt = new SchedulingContext();
			schedCtxt.InstanceId = schedulerInstanceId;

			String uid = QuartzSchedulerResources.GetUniqueIdentifier(schedulerName, schedulerInstanceId);

			RemoteScheduler remoteScheduler = new RemoteScheduler(schedCtxt, uid, rmiHost, rmiPort);

			SchedulerRepository schedRep = SchedulerRepository.Instance;
			schedRep.Bind(remoteScheduler);
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
			CreateScheduler(DEFAULT_SCHEDULER_NAME, DEFAULT_INSTANCE_ID, threadPool, jobStore);
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
		public virtual void CreateScheduler(String schedulerName, string schedulerInstanceId, IThreadPool threadPool,
		                                    IJobStore jobStore)
		{
			CreateScheduler(schedulerName, schedulerInstanceId, threadPool, jobStore, null, 0, - 1, - 1);
		}

		/// <summary>
		/// Creates a scheduler using the specified thread pool and job store and
		/// binds it to RMI.
		/// </summary>
		/// <param name="schedulerName">The name for the scheduler.</param>
		/// <param name="schedulerInstanceId">The instance ID for the scheduler.</param>
		/// <param name="threadPool">The thread pool for executing jobs</param>
		/// <param name="jobStore">The type of job store</param>
		/// <param name="rmiRegistryHost">The hostname to register this scheduler with for RMI. Can use
		/// "null" if no RMI is required.</param>
		/// <param name="rmiRegistryPort">The port for RMI. Typically 1099.</param>
		/// <param name="idleWaitTime">The idle wait time in milliseconds. You can specify "-1" for
		/// the default value, which is currently 30000 ms.</param>
		/// <param name="dbFailureRetryInterval">The db failure retry interval.</param>
		public virtual void CreateScheduler(String schedulerName, string schedulerInstanceId, IThreadPool threadPool,
		                                    IJobStore jobStore, string rmiRegistryHost, int rmiRegistryPort, long idleWaitTime,
		                                    int dbFailureRetryInterval)
		{
			// Currently only one run-shell factory is available...
			IJobRunShellFactory jrsf = new StdJobRunShellFactory();

			// Fire everything up
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			SchedulingContext schedCtxt = new SchedulingContext();
			schedCtxt.InstanceId = schedulerInstanceId;

			QuartzSchedulerResources qrs = new QuartzSchedulerResources();

			qrs.Name = schedulerName;
			qrs.InstanceId = schedulerInstanceId;
			qrs.JobRunShellFactory = jrsf;
			qrs.ThreadPool = threadPool;
			qrs.JobStore = jobStore;
			qrs.RMIRegistryHost = rmiRegistryHost;
			qrs.RMIRegistryPort = rmiRegistryPort;

			QuartzScheduler qs = new QuartzScheduler(qrs, schedCtxt, idleWaitTime, dbFailureRetryInterval);

			IClassLoadHelper cch = new CascadingClassLoadHelper();
			cch.Initialize();

			jobStore.Initialize(cch, qs.SchedulerSignaler);

			IScheduler scheduler = new StdScheduler(qs, schedCtxt);

			jrsf.Initialize(scheduler, schedCtxt);

			Log.Info("Quartz scheduler '" + scheduler.SchedulerName);

			Log.Info("Quartz scheduler version: " + qs.Version);

			SchedulerRepository schedRep = SchedulerRepository.Instance;

			qs.AddNoGCObject(schedRep); // prevents the repository from being
			// garbage collected

			schedRep.Bind(scheduler);
		}

		/*
		* public void registerSchedulerForRmi(String schedulerName, String
		* schedulerId, string registryHost, int registryPort) throws
		* SchedulerException, RemoteException { QuartzScheduler scheduler =
		* (QuartzScheduler) this.getScheduler(); scheduler.bind(registryHost,
		* registryPort); }
		*/

		/// <summary>
		/// Returns a handle to the Scheduler produced by this factory.
		/// <p>
		/// you must call createRemoteScheduler or createScheduler methods before
		/// calling getScheduler()
		/// </p>
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

			return schedRep.Lookup(DEFAULT_SCHEDULER_NAME);
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