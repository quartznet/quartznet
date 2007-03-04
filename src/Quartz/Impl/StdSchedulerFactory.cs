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
using System.Collections.Specialized;
using System.Configuration;
using System.Data.OleDb;
using System.Reflection;
using Common.Logging;
using Quartz.Collection;
using Quartz.Core;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl
{
	/// <summary> <p>
	/// An implementation of <code>{@link quartz.SchedulerFactory}</code> that
	/// does all of it's work of creating a <code>QuartzScheduler</code> instance
	/// based on the contenents of a <code>Properties</code> file.
	/// </p>
	/// 
	/// <p>
	/// By default a properties file named "quartz.properties" is loaded from the
	/// 'current working directory'. If that fails, then the "quartz.properties"
	/// file located (as a resource) in the org/quartz package is loaded. If you
	/// wish to use a file other than these defaults, you must define the system
	/// property 'quartz.properties' to* point to the file you want.
	/// </p>
	/// 
	/// <p>
	/// See the sample properties files that are distributed with Quartz for
	/// information about the various settings available within the file.
	/// </p>
	/// 
	/// <p>
	/// Alternativly, you can explicitly Initialize the factory by calling one of
	/// the <code>Initialize(xx)</code> methods before calling <code>getScheduler()</code>.
	/// </p>
	/// 
	/// <p>
	/// Instances of the specified <code>{@link quartz.spi.JobStore}</code>,
	/// <code>{@link quartz.spi.ThreadPool}</code>, classes will be created
	/// by name, and then any additional properties specified for them in the config
	/// file will be set on the instance by calling an equivalent 'set' method. For
	/// example if the properties file contains the property 'quartz.jobStore.
	/// myProp = 10' then after the JobStore class has been instantiated, the method
	/// 'setMyProp()' will be called on it. Type conversion to primitive Java types
	/// (int, long, float, double, boolean, and String) are performed before calling
	/// the propertie's setter method.
	/// </p>
	/// 
	/// </summary>
	/// <author>James House</author>
	/// <author>Anthony Eden</author>
	/// <author>Mohammad Rezaei</author>
	/// <author>Marko Lahma (.NET)</author>
	public class StdSchedulerFactory : ISchedulerFactory
	{
		private static ILog Log = LogManager.GetLogger(typeof (StdSchedulerFactory));

		private string SchedulerName
		{
			get { return cfg.GetStringProperty(PROP_SCHED_INSTANCE_NAME, "QuartzScheduler"); }
		}

		private string SchedulerInstId
		{
			get { return cfg.GetStringProperty(PROP_SCHED_INSTANCE_ID, DEFAULT_INSTANCE_ID); }
		}

		/// <summary>
		/// Returns a handle to the default Scheduler, creating it if it does not
		/// yet exist.
		/// </summary>
		/// <seealso cref="Initialize()">
		/// </seealso>
		public static IScheduler DefaultScheduler
		{
			get
			{
				StdSchedulerFactory fact = new StdSchedulerFactory();

				return fact.GetScheduler();
			}
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


		public const string PROPERTIES_FILE = "quartz.properties";
		public const string PROP_SCHED_INSTANCE_NAME = "quartz.scheduler.instanceName";
		public const string PROP_SCHED_INSTANCE_ID = "quartz.scheduler.instanceId";
		public const string PROP_SCHED_INSTANCE_ID_GENERATOR_CLASS = "quartz.scheduler.instanceIdGenerator.class";
		public const string PROP_SCHED_THREAD_NAME = "quartz.scheduler.threadName";
		public const string PROP_SCHED_RMI_EXPORT = "quartz.scheduler.rmi.export";
		public const string PROP_SCHED_RMI_PROXY = "quartz.scheduler.rmi.proxy";
		public const string PROP_SCHED_RMI_HOST = "quartz.scheduler.rmi.registryHost";
		public const string PROP_SCHED_RMI_PORT = "quartz.scheduler.rmi.registryPort";
		public const string PROP_SCHED_RMI_SERVER_PORT = "quartz.scheduler.rmi.serverPort";
		public const string PROP_SCHED_RMI_CREATE_REGISTRY = "quartz.scheduler.rmi.createRegistry";
		public const string PROP_SCHED_WRAP_JOB_IN_USER_TX = "quartz.scheduler.wrapJobExecutionInUserTransaction";
		public const string PROP_SCHED_USER_TX_URL = "quartz.scheduler.userTransactionURL";
		public const string PROP_SCHED_IDLE_WAIT_TIME = "quartz.scheduler.idleWaitTime";
		public const string PROP_SCHED_DB_FAILURE_RETRY_INTERVAL = "quartz.scheduler.dbFailureRetryInterval";
		public const string PROP_SCHED_CLASS_LOAD_HELPER_CLASS = "quartz.scheduler.classLoadHelper.class";
		public const string PROP_SCHED_JOB_FACTORY_CLASS = "quartz.scheduler.jobFactory.class";
		public const string PROP_SCHED_JOB_FACTORY_PREFIX = "quartz.scheduler.jobFactory";
		public const string PROP_SCHED_CONTEXT_PREFIX = "quartz.context.key";
		public const string PROP_THREAD_POOL_PREFIX = "quartz.threadPool";
		public const string PROP_THREAD_POOL_CLASS = "quartz.threadPool.class";
		public const string PROP_JOB_STORE_PREFIX = "quartz.jobStore";
		public const string PROP_JOB_STORE_CLASS = "quartz.jobStore.class";
		public const string PROP_JOB_STORE_USE_PROP = "quartz.jobStore.useProperties";
		public const string PROP_DATASOURCE_PREFIX = "quartz.dataSource";
		public const string PROP_CONNECTION_PROVIDER_CLASS = "connectionProvider.class";
		public const string PROP_DATASOURCE_DRIVER = "driver";
		public const string PROP_DATASOURCE_URL = "URL";
		public const string PROP_DATASOURCE_USER = "user";
		public const string PROP_DATASOURCE_PASSWORD = "password";
		public const string PROP_DATASOURCE_MAX_CONNECTIONS = "maxConnections";
		public const string PROP_DATASOURCE_VALIDATION_QUERY = "validationQuery";
		public const string PROP_DATASOURCE_JNDI_URL = "jndiURL";
		public const string PROP_DATASOURCE_JNDI_ALWAYS_LOOKUP = "jndiAlwaysLookup";
		public const string PROP_DATASOURCE_JNDI_INITIAL = "java.naming.factory.initial";
		public const string PROP_DATASOURCE_JNDI_PROVDER = "java.naming.provider.url";
		public const string PROP_DATASOURCE_JNDI_PRINCIPAL = "java.naming.security.principal";
		public const string PROP_DATASOURCE_JNDI_CREDENTIALS = "java.naming.security.credentials";
		public const string PROP_PLUGIN_PREFIX = "quartz.plugin";
		public const string PROP_PLUGIN_CLASS = "class";
		public const string PROP_JOB_LISTENER_PREFIX = "quartz.jobListener";
		public const string PROP_TRIGGER_LISTENER_PREFIX = "quartz.triggerListener";
		public const string PROP_LISTENER_CLASS = "class";
		public const string DEFAULT_INSTANCE_ID = "NON_CLUSTERED";
		public const string AUTO_GENERATE_INSTANCE_ID = "AUTO";
		private SchedulerException initException = null;

		private PropertiesParser cfg;

		public StdSchedulerFactory()
		{
		}

		public StdSchedulerFactory(NameValueCollection props)
		{
			Initialize(props);
		}

		/// <summary>
		/// Initialize the <code>SchedulerFactory</code> with
		/// the contenents of a <code>Properties</code> file.
		/// 
		/// <p>
		/// By default a properties file named "quartz.properties" is loaded from
		/// the 'current working directory'. If that fails, then the
		/// "quartz.properties" file located (as a resource) in the org/quartz
		/// package is loaded. If you wish to use a file other than these defaults,
		/// you must define the system property 'quartz.properties' to point to
		/// the file you want.
		/// </p>
		/// 
		/// <p>
		/// System properties (envrionment variables, and -D definitions on the
		/// command-line when running the JVM) over-ride any properties in the
		/// loaded file.
		/// </p>
		/// </summary>
		public void Initialize()
		{
			// short-circuit if already initialized
			if (cfg != null)
			{
				return;
			}
			if (initException != null)
			{
				throw initException;
			}

#if NET_20
            NameValueCollection props = (NameValueCollection)System.Configuration.ConfigurationManager.GetSection("quartz");
#else
			NameValueCollection props = (NameValueCollection) ConfigurationSettings.GetConfig("quartz");
#endif
			if (props == null)
			{
				throw new SchedulerConfigException("Could not find <quartz> configuration section from your application config. Please add it to correctly Initialize Quartz.");
			}
			Initialize(OverrideWithSysProps(props));
		}

		/// <summary>
		/// Creates a new name value collection and overrides its values
		/// with system values (environment variables).
		/// </summary>
		/// <param name="props">The base properties to override.</param>
		/// <returns>A new NameValueCollection instance.</returns>
		private static NameValueCollection OverrideWithSysProps(NameValueCollection props)
		{
			NameValueCollection retValue = new NameValueCollection(props);
			ICollection keys = Environment.GetEnvironmentVariables().Keys;

			foreach (string key in keys)
			{
				retValue.Set(key, props[key]);
			}
			return retValue;
		}



		/// <summary> 
		/// Initialize the <code>SchedulerFactory</code> with
		/// the contenents of the given <code>Properties</code> object.
		/// </summary>
		public virtual void Initialize(NameValueCollection props)
		{
			cfg = new PropertiesParser(props);
		}

		/// <summary>  </summary>
		private IScheduler Instantiate()
		{
			if (cfg == null)
			{
				Initialize();
			}

			if (initException != null)
			{
				throw initException;
			}

			IJobStore js = null;
			IThreadPool tp = null;
			QuartzScheduler qs = null;
			SchedulingContext schedCtxt = null;
			DBConnectionManager dbMgr = null;
			string instanceIdGeneratorClass = null;
			NameValueCollection tProps = null;
			String userTXLocation = null;
			bool wrapJobInTx = false;
			bool autoId = false;
			long idleWaitTime = - 1;
			int dbFailureRetry = - 1;
			string classLoadHelperClass;
			string jobFactoryClass;

			SchedulerRepository schedRep = SchedulerRepository.Instance;

			// Get Scheduler Properties
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			string schedName = cfg.GetStringProperty(PROP_SCHED_INSTANCE_NAME, "QuartzScheduler");
			string threadName = cfg.GetStringProperty(PROP_SCHED_THREAD_NAME, schedName + "_QuartzSchedulerThread");
			string schedInstId = cfg.GetStringProperty(PROP_SCHED_INSTANCE_ID, DEFAULT_INSTANCE_ID);

			if (schedInstId.Equals(AUTO_GENERATE_INSTANCE_ID))
			{
				autoId = true;
				instanceIdGeneratorClass =
					cfg.GetStringProperty(PROP_SCHED_INSTANCE_ID_GENERATOR_CLASS, "Quartz.Simpl.SimpleInstanceIdGenerator, Quartz");
			}

			userTXLocation = cfg.GetStringProperty(PROP_SCHED_USER_TX_URL, userTXLocation);
			if (userTXLocation != null && userTXLocation.Trim().Length == 0)
			{
				userTXLocation = null;
			}

			classLoadHelperClass =
				cfg.GetStringProperty(PROP_SCHED_CLASS_LOAD_HELPER_CLASS, "Quartz.Simpl.CascadingClassLoadHelper, Quartz");
			wrapJobInTx = cfg.GetBooleanProperty(PROP_SCHED_WRAP_JOB_IN_USER_TX, wrapJobInTx);

			jobFactoryClass = cfg.GetStringProperty(PROP_SCHED_JOB_FACTORY_CLASS, null);

			idleWaitTime = cfg.GetLongProperty(PROP_SCHED_IDLE_WAIT_TIME, idleWaitTime);
			dbFailureRetry = cfg.GetIntProperty(PROP_SCHED_DB_FAILURE_RETRY_INTERVAL, dbFailureRetry);

			bool rmiExport = cfg.GetBooleanProperty(PROP_SCHED_RMI_EXPORT, false);
			bool rmiProxy = cfg.GetBooleanProperty(PROP_SCHED_RMI_PROXY, false);
			String rmiHost = cfg.GetStringProperty(PROP_SCHED_RMI_HOST, "localhost");
			int rmiPort = cfg.GetIntProperty(PROP_SCHED_RMI_PORT, 1099);
			int rmiServerPort = cfg.GetIntProperty(PROP_SCHED_RMI_SERVER_PORT, - 1);
			String rmiCreateRegistry =
				cfg.GetStringProperty(PROP_SCHED_RMI_CREATE_REGISTRY, QuartzSchedulerResources.CREATE_REGISTRY_NEVER);

			NameValueCollection schedCtxtProps = cfg.GetPropertyGroup(PROP_SCHED_CONTEXT_PREFIX, true);

			// If Proxying to remote scheduler, short-circuit here...
			// ~~~~~~~~~~~~~~~~~~
			if (rmiProxy)
			{
				if (autoId)
				{
					schedInstId = DEFAULT_INSTANCE_ID;
				}

				schedCtxt = new SchedulingContext();
				schedCtxt.InstanceId = schedInstId;

				String uid = QuartzSchedulerResources.GetUniqueIdentifier(schedName, schedInstId);

				RemoteScheduler remoteScheduler = new RemoteScheduler(schedCtxt, uid, rmiHost, rmiPort);

				schedRep.Bind(remoteScheduler);

				return remoteScheduler;
			}

			// Create class load helper
			IClassLoadHelper loadHelper = null;
			try
			{
				loadHelper = (IClassLoadHelper) Activator.CreateInstance(LoadClass(classLoadHelperClass));
			}
			catch (Exception e)
			{
				throw new SchedulerConfigException("Unable to Instantiate class load helper class: " + e.Message, e);
			}
			loadHelper.Initialize();

			IJobFactory jobFactory = null;
			if (jobFactoryClass != null)
			{
				try
				{
					jobFactory = (IJobFactory) ObjectUtils.InstantiateType(loadHelper.LoadClass(jobFactoryClass));
				}
				catch (Exception e)
				{
					throw new SchedulerConfigException("Unable to Instantiate JobFactory class: " + e.Message, e);
				}

				tProps = cfg.GetPropertyGroup(PROP_SCHED_JOB_FACTORY_PREFIX, true);
				try
				{
					ObjectUtils.SetObjectProperties(jobFactory, tProps);
				}
				catch (Exception e)
				{
					initException =
						new SchedulerException("JobFactory class '" + jobFactoryClass + "' props could not be configured.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
			}

			IInstanceIdGenerator instanceIdGenerator = null;
			if (instanceIdGeneratorClass != null)
			{
				try
				{
					instanceIdGenerator =
						(IInstanceIdGenerator) ObjectUtils.InstantiateType(loadHelper.LoadClass(instanceIdGeneratorClass));
				}
				catch (Exception e)
				{
					throw new SchedulerConfigException("Unable to Instantiate InstanceIdGenerator class: " + e.Message, e);
				}
			}

			// Get ThreadPool Properties
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			String tpClass = cfg.GetStringProperty(PROP_THREAD_POOL_CLASS, null);

			if (tpClass == null)
			{
				initException = new SchedulerException("ThreadPool class not specified. ", SchedulerException.ERR_BAD_CONFIGURATION);
				throw initException;
			}

			try
			{
				tp = (IThreadPool) ObjectUtils.InstantiateType(loadHelper.LoadClass(tpClass));
			}
			catch (Exception e)
			{
				initException = new SchedulerException("ThreadPool class '" + tpClass + "' could not be instantiated.", e);
				initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
				throw initException;
			}
			tProps = cfg.GetPropertyGroup(PROP_THREAD_POOL_PREFIX, true);
			try
			{
				ObjectUtils.SetObjectProperties(tp, tProps);
			}
			catch (Exception e)
			{
				initException = new SchedulerException("ThreadPool class '" + tpClass + "' props could not be configured.", e);
				initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
				throw initException;
			}

			// Get JobStore Properties
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			string jsClass = cfg.GetStringProperty(PROP_JOB_STORE_CLASS, typeof (RAMJobStore).FullName);

			if (jsClass == null)
			{
				initException = new SchedulerException("JobStore class not specified. ", SchedulerException.ERR_BAD_CONFIGURATION);
				throw initException;
			}

			try
			{
				js = (IJobStore) ObjectUtils.InstantiateType(loadHelper.LoadClass(jsClass));
			}
			catch (Exception e)
			{
				initException = new SchedulerException("JobStore class '" + jsClass + "' could not be instantiated.", e);
				initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
				throw initException;
			}
			tProps = cfg.GetPropertyGroup(PROP_JOB_STORE_PREFIX, true);
			try
			{
				ObjectUtils.SetObjectProperties(js, tProps);
			}
			catch (Exception e)
			{
				initException = new SchedulerException("JobStore class '" + jsClass + "' props could not be configured.", e);
				initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
				throw initException;
			}

			if (js is JobStoreSupport)
			{
				((JobStoreSupport) js).InstanceId = schedInstId;
				((JobStoreSupport) js).InstanceName = schedName;
			}

			// Set up any DataSources
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			string[] dsNames = cfg.GetPropertyGroups(PROP_DATASOURCE_PREFIX);
			for (int i = 0; i < dsNames.Length; i++)
			{
				PropertiesParser pp = new PropertiesParser(cfg.GetPropertyGroup(PROP_DATASOURCE_PREFIX + "." + dsNames[i], true));

				String cpClass = pp.GetStringProperty(PROP_CONNECTION_PROVIDER_CLASS, null);

				// custom connectionProvider...
				if (cpClass != null)
				{
					IConnectionProvider cp = null;
					try
					{
						cp = (IConnectionProvider) ObjectUtils.InstantiateType(loadHelper.LoadClass(cpClass));
					}
					catch (Exception e)
					{
						initException = new SchedulerException("ConnectionProvider class '" + cpClass + "' could not be instantiated.", e);
						initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
						throw initException;
					}

					try
					{
						// remove the class name, so it isn't attempted to be set
						pp.UnderlyingProperties.Remove(PROP_CONNECTION_PROVIDER_CLASS);

						ObjectUtils.SetObjectProperties(cp, pp.UnderlyingProperties);
					}
					catch (Exception e)
					{
						initException =
							new SchedulerException("ConnectionProvider class '" + cpClass + "' props could not be configured.", e);
						initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
						throw initException;
					}

					dbMgr = DBConnectionManager.Instance;
					dbMgr.AddConnectionProvider(dsNames[i], cp);
				}
				else
				{
					String dsDriver = pp.GetStringProperty(PROP_DATASOURCE_DRIVER, null);
					String dsURL = pp.GetStringProperty(PROP_DATASOURCE_URL, null);
					bool dsAlwaysLookup = pp.GetBooleanProperty(PROP_DATASOURCE_JNDI_ALWAYS_LOOKUP, false);
					String dsUser = pp.GetStringProperty(PROP_DATASOURCE_USER, "");
					String dsPass = pp.GetStringProperty(PROP_DATASOURCE_PASSWORD, "");
					int dsCnt = pp.GetIntProperty(PROP_DATASOURCE_MAX_CONNECTIONS, 10);
					String dsJndi = pp.GetStringProperty(PROP_DATASOURCE_JNDI_URL, null);
					String dsJndiInitial = pp.GetStringProperty(PROP_DATASOURCE_JNDI_INITIAL, null);
					String dsJndiProvider = pp.GetStringProperty(PROP_DATASOURCE_JNDI_PROVDER, null);
					String dsJndiPrincipal = pp.GetStringProperty(PROP_DATASOURCE_JNDI_PRINCIPAL, null);
					String dsJndiCredentials = pp.GetStringProperty(PROP_DATASOURCE_JNDI_CREDENTIALS, null);
					String dsValidation = pp.GetStringProperty(PROP_DATASOURCE_VALIDATION_QUERY, null);

					if (dsJndi != null)
					{
						NameValueCollection props = null;
						if (null != dsJndiInitial || null != dsJndiProvider || null != dsJndiPrincipal || null != dsJndiCredentials)
						{
							props = new NameValueCollection();
							if (dsJndiInitial != null)
							{
								props[PROP_DATASOURCE_JNDI_INITIAL] = dsJndiInitial;
							}
							if (dsJndiProvider != null)
							{
								props[PROP_DATASOURCE_JNDI_PROVDER] = dsJndiProvider;
							}
							if (dsJndiPrincipal != null)
							{
								props[PROP_DATASOURCE_JNDI_PRINCIPAL] = dsJndiPrincipal;
							}
							if (dsJndiCredentials != null)
							{
								props[PROP_DATASOURCE_JNDI_CREDENTIALS] = dsJndiCredentials;
							}
						}
						// TODO JNDIConnectionProvider cp = new JNDIConnectionProvider(dsJndi, props, dsAlwaysLookup);
						dbMgr = DBConnectionManager.Instance;
						dbMgr.AddConnectionProvider(dsNames[i], null); //cp);
					}
					else
					{
						if (dsDriver == null)
						{
							initException = new SchedulerException("Driver not specified for DataSource: " + dsNames[i]);
							throw initException;
						}
						if (dsURL == null)
						{
							initException = new SchedulerException("DB URL not specified for DataSource: " + dsNames[i]);
							throw initException;
						}
						try
						{
							/*TODO 
							 PoolingConnectionProvider cp =
								new PoolingConnectionProvider(dsDriver, dsURL, dsUser, dsPass, dsCnt, dsValidation);
							*/
							dbMgr = DBConnectionManager.Instance;
							dbMgr.AddConnectionProvider(dsNames[i], null); //cp);
						}
						catch (OleDbException sqle)
						{
							initException = new SchedulerException("Could not Initialize DataSource: " + dsNames[i], sqle);
							throw initException;
						}
					}
				}
			}

			// Set up any SchedulerPlugins
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			string[] pluginNames = cfg.GetPropertyGroups(PROP_PLUGIN_PREFIX);
			ISchedulerPlugin[] plugins = new ISchedulerPlugin[pluginNames.Length];
			for (int i = 0; i < pluginNames.Length; i++)
			{
				NameValueCollection pp = cfg.GetPropertyGroup(PROP_PLUGIN_PREFIX + "." + pluginNames[i], true);

				String plugInClass = pp[PROP_PLUGIN_CLASS] == null ? null : pp[PROP_PLUGIN_CLASS];

				if (plugInClass == null)
				{
					initException =
						new SchedulerException("SchedulerPlugin class not specified for plugin '" + pluginNames[i] + "'",
						                       SchedulerException.ERR_BAD_CONFIGURATION);
					throw initException;
				}
				ISchedulerPlugin plugin = null;
				try
				{
					// TODO
					// plugin = (ISchedulerPlugin) Activator.CreateInstance(loadHelper.(plugInClass));
				}
				catch (Exception e)
				{
					initException = new SchedulerException("SchedulerPlugin class '" + plugInClass + "' could not be instantiated.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				try
				{
					ObjectUtils.SetObjectProperties(plugin, pp);
				}
				catch (Exception e)
				{
					initException =
						new SchedulerException("JobStore SchedulerPlugin '" + plugInClass + "' props could not be configured.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				plugins[i] = plugin;
			}

			// Set up any JobListeners
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			Type[] strArg = new Type[] {typeof (String)};
			string[] jobListenerNames = cfg.GetPropertyGroups(PROP_JOB_LISTENER_PREFIX);
			IJobListener[] jobListeners = new IJobListener[jobListenerNames.Length];
			for (int i = 0; i < jobListenerNames.Length; i++)
			{
				NameValueCollection lp = cfg.GetPropertyGroup(PROP_JOB_LISTENER_PREFIX + "." + jobListenerNames[i], true);

				String listenerClass = lp[PROP_LISTENER_CLASS] == null ? null : lp[PROP_LISTENER_CLASS];

				if (listenerClass == null)
				{
					initException =
						new SchedulerException("JobListener class not specified for listener '" + jobListenerNames[i] + "'",
						                       SchedulerException.ERR_BAD_CONFIGURATION);
					throw initException;
				}
				IJobListener listener = null;
				try
				{
					listener = (IJobListener) ObjectUtils.InstantiateType(loadHelper.LoadClass(listenerClass));
				}
				catch (Exception e)
				{
					initException = new SchedulerException("JobListener class '" + listenerClass + "' could not be instantiated.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				try
				{
					MethodInfo nameSetter = listener.GetType().GetMethod("setName", (strArg == null) ? new Type[0] : (Type[]) strArg);
					if (nameSetter != null)
					{
						nameSetter.Invoke(listener, new object[] {jobListenerNames[i]});
					}
					ObjectUtils.SetObjectProperties(listener, lp);
				}
				catch (Exception e)
				{
					initException = new SchedulerException("JobListener '" + listenerClass + "' props could not be configured.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				jobListeners[i] = listener;
			}

			// Set up any TriggerListeners
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			string[] triggerListenerNames = cfg.GetPropertyGroups(PROP_TRIGGER_LISTENER_PREFIX);
			ITriggerListener[] triggerListeners = new ITriggerListener[triggerListenerNames.Length];
			for (int i = 0; i < triggerListenerNames.Length; i++)
			{
				NameValueCollection lp = cfg.GetPropertyGroup(PROP_TRIGGER_LISTENER_PREFIX + "." + triggerListenerNames[i], true);

				String listenerClass = lp[PROP_LISTENER_CLASS] == null ? null : lp[PROP_LISTENER_CLASS];

				if (listenerClass == null)
				{
					initException =
						new SchedulerException("TriggerListener class not specified for listener '" + triggerListenerNames[i] + "'",
						                       SchedulerException.ERR_BAD_CONFIGURATION);
					throw initException;
				}
				ITriggerListener listener = null;
				try
				{
					listener = (ITriggerListener) ObjectUtils.InstantiateType(loadHelper.LoadClass(listenerClass));
				}
				catch (Exception e)
				{
					initException =
						new SchedulerException("TriggerListener class '" + listenerClass + "' could not be instantiated.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				try
				{
					MethodInfo nameSetter = listener.GetType().GetMethod("setName", (strArg == null) ? new Type[0] :  strArg);
					if (nameSetter != null)
					{
						nameSetter.Invoke(listener, (new object[] {triggerListenerNames[i]}));
					}
					ObjectUtils.SetObjectProperties(listener, lp);
				}
				catch (Exception e)
				{
					initException = new SchedulerException("TriggerListener '" + listenerClass + "' props could not be configured.", e);
					initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
					throw initException;
				}
				triggerListeners[i] = listener;
			}


			// Fire everything up
			// ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

			IJobRunShellFactory jrsf = null; // Create correct run-shell factory...
			// TODO UserTransactionHelper userTxHelper = null;

			if (wrapJobInTx)
			{
				//  TODO userTxHelper = new UserTransactionHelper(userTXLocation);
			}

			if (wrapJobInTx)
			{
				// TODO jrsf = new JTAJobRunShellFactory(userTxHelper);
			}
			else
			{
				jrsf = new StdJobRunShellFactory();
			}

			if (autoId)
			{
				try
				{
					schedInstId = DEFAULT_INSTANCE_ID;
					if (js is JobStoreSupport)
					{
						if (((JobStoreSupport) js).Clustered)
						{
							schedInstId = instanceIdGenerator.GenerateInstanceId();
						}
					}
				}
				catch (Exception e)
				{
					Log.Error("Couldn't generate instance Id!", e);
					throw new SystemException("Cannot run without an instance id.");
				}
			}

			if (js is JobStoreSupport)
			{
				JobStoreSupport jjs = (JobStoreSupport) js;
				jjs.InstanceId = schedInstId;
				jjs.DbRetryInterval = dbFailureRetry;
			}

			QuartzSchedulerResources rsrcs = new QuartzSchedulerResources();
			rsrcs.Name = schedName;
			rsrcs.ThreadName = threadName;
			rsrcs.InstanceId = schedInstId;
			rsrcs.JobRunShellFactory = jrsf;

			if (rmiExport)
			{
				rsrcs.RMIRegistryHost = rmiHost;
				rsrcs.RMIRegistryPort = rmiPort;
				rsrcs.RMIServerPort = rmiServerPort;
				rsrcs.RMICreateRegistryStrategy = rmiCreateRegistry;
			}

			rsrcs.ThreadPool = tp;
			if (tp is SimpleThreadPool)
			{
				((SimpleThreadPool) tp).ThreadNamePrefix = schedName + "_Worker";
			}
			tp.Initialize();

			rsrcs.JobStore = js;

			schedCtxt = new SchedulingContext();
			schedCtxt.InstanceId = rsrcs.InstanceId;

			qs = new QuartzScheduler(rsrcs, schedCtxt, idleWaitTime, dbFailureRetry);

			//    if(usingJSCMT)
			//      qs.setSignalOnSchedulingChange(false); // TODO: fixed? (don't need
			// this any more?)

			// Create Scheduler ref...
			IScheduler sched = Instantiate(rsrcs, qs);

			// set job factory if specified
			if (jobFactory != null)
			{
				qs.JobFactory = jobFactory;
			}

			// add plugins
			for (int i = 0; i < plugins.Length; i++)
			{
				plugins[i].Initialize(pluginNames[i], sched);
				qs.AddSchedulerPlugin(plugins[i]);
			}

			// add listeners
			for (int i = 0; i < jobListeners.Length; i++)
			{
				qs.AddGlobalJobListener(jobListeners[i]);
			}
			for (int i = 0; i < triggerListeners.Length; i++)
			{
				qs.AddGlobalTriggerListener(triggerListeners[i]);
			}

			// set scheduler context data...
			IEnumerator itr = new HashSet(schedCtxtProps).GetEnumerator();
			while (itr.MoveNext())
			{
				String key = (String) itr.Current;
				String val = schedCtxtProps.Get(key);

				sched.Context.Put(key, val);
			}

			// fire up job store, and runshell factory

			js.Initialize(loadHelper, qs.SchedulerSignaler);

			jrsf.Initialize(sched, schedCtxt);

			Log.Info(string.Format("Quartz scheduler '{0}' initialized", sched.SchedulerName));

			Log.Info(string.Format("Quartz scheduler version: {0}", qs.Version));

			// prevents the repository from being garbage collected
			qs.AddNoGCObject(schedRep);
			// prevents the db manager from being garbage collected
			if (dbMgr != null)
			{
				qs.AddNoGCObject(dbMgr);
			}

			schedRep.Bind(sched);

			return sched;
		}

		protected internal virtual IScheduler Instantiate(QuartzSchedulerResources rsrcs, QuartzScheduler qs)
		{
			SchedulingContext schedCtxt = new SchedulingContext();
			schedCtxt.InstanceId = rsrcs.InstanceId;

			IScheduler sched = new StdScheduler(qs, schedCtxt);
			return sched;
		}


		private Type LoadClass(string className)
		{
			return Type.GetType(className);
		}

		/// <summary> <p>
		/// Returns a handle to the Scheduler produced by this factory.
		/// </p>
		/// 
		/// <p>
		/// If one of the <code>Initialize</code> methods has not be previously
		/// called, then the default (no-arg) <code>Initialize()</code> method
		/// will be called by this method.
		/// </p>
		/// </summary>
		public virtual IScheduler GetScheduler()
		{
			if (cfg == null)
			{
				Initialize();
			}

			SchedulerRepository schedRep = SchedulerRepository.Instance;

			IScheduler sched = schedRep.Lookup(SchedulerName);

			if (sched != null)
			{
				if (sched.IsShutdown)
				{
					schedRep.Remove(SchedulerName);
				}
				else
				{
					return sched;
				}
			}

			sched = Instantiate();

			return sched;
		}

		/// <summary> <p>
		/// Returns a handle to the Scheduler with the given name, if it exists (if
		/// it has already been instantiated).
		/// </p>
		/// </summary>
		public virtual IScheduler GetScheduler(String schedName)
		{
			return SchedulerRepository.Instance.Lookup(schedName);
		}
	}

	// TODO bring JDBC job store
	internal class JobStoreSupport
	{
		public string InstanceId
		{
			get { return null; }
			set
			{
			}
		}

		public string InstanceName
		{
			get { return null; }
			set
			{
			}
		}

		public bool Clustered
		{
			get { throw new NotImplementedException(); }
		}

		public long DbRetryInterval
		{
			get { return -1; }
			set
			{
			}
		}
	}
}
