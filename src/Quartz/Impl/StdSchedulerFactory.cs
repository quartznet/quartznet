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
using System.Reflection;

using Common.Logging;

using Quartz.Collection;
using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl
{
    /// <summary>
    /// An implementation of <see cref="ISchedulerFactory" /> that
    /// does all of it's work of creating a <see cref="QuartzScheduler" /> instance
    /// based on the contenents of a properties file.
    /// </summary>
    /// <remarks>
    /// <p>
    /// By default a properties are loaded from App.config's quartz section. 
    /// If that fails, then the "quartz.properties"
    /// file located (as a embedded resource) in Quartz.dll is loaded. If you
    /// wish to use a file other than these defaults, you must define the system
    /// property 'quartz.properties' to* point to the file you want.
    /// </p>
    /// 
    /// <p>
    /// See the sample properties that are distributed with Quartz for
    /// information about the various settings available within the file.
    /// </p>
    /// 
    /// <p>
    /// Alternativly, you can explicitly Initialize the factory by calling one of
    /// the <see cref="Initialize()" /> methods before calling <see cref="GetScheduler()" />.
    /// </p>
    /// 
    /// <p>
    /// Instances of the specified <see cref="IJobStore" />,
    /// <see cref="IThreadPool" />, classes will be created
    /// by name, and then any additional properties specified for them in the config
    /// file will be set on the instance by calling an equivalent 'set' method. For
    /// example if the properties file contains the property 'quartz.jobStore.
    /// myProp = 10' then after the JobStore class has been instantiated, the method
    /// 'setMyProp()' will be called on it. Type conversion to primitive Java types
    /// (int, long, float, double, boolean, and String) are performed before calling
    /// the propertie's setter method.
    /// </p>
    /// </remarks>
    /// <author>James House</author>
    /// <author>Anthony Eden</author>
    /// <author>Mohammad Rezaei</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdSchedulerFactory : ISchedulerFactory
    {
        public const string PROPERTIES_FILE = "quartz.properties";
        public const string PROP_SCHED_INSTANCE_NAME = "quartz.scheduler.instanceName";
        public const string PROP_SCHED_INSTANCE_ID = "quartz.scheduler.instanceId";
        public const string PROP_SCHED_INSTANCE_ID_GENERATOR_PREFIX = "quartz.scheduler.instanceIdGenerator";
        public const string PROP_SCHED_INSTANCE_ID_GENERATOR_TYPE = PROP_SCHED_INSTANCE_ID_GENERATOR_PREFIX + ".type";
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
        public const string PROP_SCHED_MAKE_SCHEDULER_THREAD_DAEMON = "quartz.scheduler.makeSchedulerThreadDaemon";
        public const string PROP_SCHED_TYPE_LOAD_HELPER_TYPE = "quartz.scheduler.typeLoadHelper.type";
        public const string PROP_SCHED_JOB_FACTORY_TYPE = "quartz.scheduler.jobFactory.type";
        public const string PROP_SCHED_JOB_FACTORY_PREFIX = "quartz.scheduler.jobFactory";
        public const string PROP_SCHED_CONTEXT_PREFIX = "quartz.context.key";
        public const string PROP_THREAD_POOL_PREFIX = "quartz.threadPool";
        public const string PROP_THREAD_POOL_TYPE = "quartz.threadPool.type";
        public const string PROP_JOB_STORE_PREFIX = "quartz.jobStore";
        public const string PROP_JOB_STORE_LOCK_HANDLER_PREFIX = PROP_JOB_STORE_PREFIX + ".lockHandler";
        public const string PROP_JOB_STORE_LOCK_HANDLER_TYPE = PROP_JOB_STORE_LOCK_HANDLER_PREFIX + ".type";
        public const string PROP_TABLE_PREFIX = "tablePrefix";
        public const string PROP_JOB_STORE_TYPE = "quartz.jobStore.type";
        public const string PROP_JOB_STORE_USE_PROP = "quartz.jobStore.useProperties";
        public const string PROP_DATASOURCE_PREFIX = "quartz.dataSource";
        public const string PROP_DB_PROVIDER_TYPE = "connectionProvider.type";
        public const string PROP_DATASOURCE_PROVIDER = "provider";
        public const string PROP_DATASOURCE_CONNECTION_STRING = "connectionString";
        public const string PROP_DATASOURCE_USER = "user";
        public const string PROP_DATASOURCE_PASSWORD = "password";
        public const string PROP_DATASOURCE_MAX_CONNECTIONS = "maxConnections";
        public const string PROP_DATASOURCE_VALIDATION_QUERY = "validationQuery";
        public const string PROP_PLUGIN_PREFIX = "quartz.plugin";
        public const string PROP_PLUGIN_TYPE = "type";
        public const string PROP_JOB_LISTENER_PREFIX = "quartz.jobListener";
        public const string PROP_TRIGGER_LISTENER_PREFIX = "quartz.triggerListener";
        public const string PROP_LISTENER_TYPE = "type";
        public const string DEFAULT_INSTANCE_ID = "NON_CLUSTERED";
        public const string AUTO_GENERATE_INSTANCE_ID = "AUTO";
        private SchedulerException initException = null;

        private PropertiesParser cfg;

        private static readonly ILog Log = LogManager.GetLogger(typeof (StdSchedulerFactory));

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
            get { return SchedulerRepository.Instance.LookupAll(); }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StdSchedulerFactory"/> class.
        /// </summary>
        public StdSchedulerFactory()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StdSchedulerFactory"/> class.
        /// </summary>
        /// <param name="props">The props.</param>
        public StdSchedulerFactory(NameValueCollection props)
        {
            Initialize(props);
        }

        /// <summary>
        /// Initialize the <see cref="ISchedulerFactory" /> with
        /// the contenents of a Properties file.
        /// 
        /// <p>
        /// By default a properties file named "quartz.properties" is loaded from
        /// the 'current working directory'. If that fails, then the
        /// "quartz.properties" file located (as a resource) in the org/quartz
        /// package is loaded. If you wish to use a file other than these defaults,
        /// you must define the system property 'quartz.properties' to point to
        /// the file you want.
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
            NameValueCollection props = (NameValueCollection) ConfigurationManager.GetSection("quartz");
#else
			NameValueCollection props = (NameValueCollection) ConfigurationSettings.GetConfig("quartz");
#endif
            if (props == null)
            {
                // read from assembly
                try
                {
                    PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource("Quartz.quartz.properties");
                    props = pp.UnderlyingProperties;
                    Log.Info("Default Quartz.NET properties loaded from embedded resource file");
                }
                catch (Exception ex)
                {
                    Log.Error(
                        string.Format("Could not load default properties for Quartz from Quartz assembly: {0}",
                                      ex.Message), ex);
                }
            }
            if (props == null)
            {
                throw new SchedulerConfigException(
                    @"Could not find <quartz> configuration section from your application config or load default configuration from assembly.
Please add configuration to your application config file to correctly initialize Quartz.");
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
        /// Initialize the <see cref="ISchedulerFactory" /> with
        /// the contenents of the given Properties object.
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

            IJobStore js;
            IThreadPool tp;
            QuartzScheduler qs;
            SchedulingContext schedCtxt;
            DBConnectionManager dbMgr = null;
            string instanceIdGeneratorType = null;
            NameValueCollection tProps;
            bool autoId = false;
            long idleWaitTime = - 1;
            int dbFailureRetry = - 1;
            string typeLoadHelperType;
            string jobFactoryType;

            SchedulerRepository schedRep = SchedulerRepository.Instance;

            // Get Scheduler Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string schedName = cfg.GetStringProperty(PROP_SCHED_INSTANCE_NAME, "QuartzScheduler");
            string threadName =
                cfg.GetStringProperty(PROP_SCHED_THREAD_NAME, string.Format("{0}_QuartzSchedulerThread", schedName));
            string schedInstId = cfg.GetStringProperty(PROP_SCHED_INSTANCE_ID, DEFAULT_INSTANCE_ID);

            if (schedInstId.Equals(AUTO_GENERATE_INSTANCE_ID))
            {
                autoId = true;
                instanceIdGeneratorType =
                    cfg.GetStringProperty(PROP_SCHED_INSTANCE_ID_GENERATOR_TYPE,
                                          "Quartz.Simpl.SimpleInstanceIdGenerator, Quartz");
            }


            typeLoadHelperType =
                cfg.GetStringProperty(PROP_SCHED_TYPE_LOAD_HELPER_TYPE,
                                      "Quartz.Simpl.CascadingClassLoadHelper, Quartz");
            jobFactoryType = cfg.GetStringProperty(PROP_SCHED_JOB_FACTORY_TYPE, null);

            idleWaitTime = cfg.GetLongProperty(PROP_SCHED_IDLE_WAIT_TIME, idleWaitTime);
            dbFailureRetry = cfg.GetIntProperty(PROP_SCHED_DB_FAILURE_RETRY_INTERVAL, dbFailureRetry);

            NameValueCollection schedCtxtProps = cfg.GetPropertyGroup(PROP_SCHED_CONTEXT_PREFIX, true);


            // Create type load helper
            ITypeLoadHelper loadHelper;
            try
            {
                loadHelper = (ITypeLoadHelper) ObjectUtils.InstantiateType(LoadType(typeLoadHelperType));
            }
            catch (Exception e)
            {
                throw new SchedulerConfigException(
                    string.Format("Unable to instantiate type load helper: {0}", e.Message), e);
            }
            loadHelper.Initialize();

            IJobFactory jobFactory = null;
            if (jobFactoryType != null)
            {
                try
                {
                    jobFactory = (IJobFactory) ObjectUtils.InstantiateType(loadHelper.LoadType(jobFactoryType));
                }
                catch (Exception e)
                {
                    throw new SchedulerConfigException(
                        string.Format("Unable to Instantiate JobFactory: {0}", e.Message), e);
                }

                tProps = cfg.GetPropertyGroup(PROP_SCHED_JOB_FACTORY_PREFIX, true);
                try
                {
                    ObjectUtils.SetObjectProperties(jobFactory, tProps);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("JobFactory of type '{0}' props could not be configured.", jobFactoryType), e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
            }

            IInstanceIdGenerator instanceIdGenerator = null;
            if (instanceIdGeneratorType != null)
            {
                try
                {
                    instanceIdGenerator =
                        (IInstanceIdGenerator)
                        ObjectUtils.InstantiateType(loadHelper.LoadType(instanceIdGeneratorType));
                }
                catch (Exception e)
                {
                    throw new SchedulerConfigException(
                        string.Format("Unable to Instantiate InstanceIdGenerator: {0}", e.Message), e);
                }
                tProps = cfg.GetPropertyGroup(PROP_SCHED_INSTANCE_ID_GENERATOR_PREFIX, true);
                try
                {
                    ObjectUtils.SetObjectProperties(instanceIdGenerator, tProps);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("InstanceIdGenerator of type '{0}' props could not be configured.",
                                          instanceIdGeneratorType), e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
            }

            // Get ThreadPool Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string tpType = cfg.GetStringProperty(PROP_THREAD_POOL_TYPE, null);

            if (tpType == null)
            {
                initException =
                    new SchedulerException("ThreadPool type not specified. ", SchedulerException.ERR_BAD_CONFIGURATION);
                throw initException;
            }

            try
            {
                tp = (IThreadPool) ObjectUtils.InstantiateType(loadHelper.LoadType(tpType));
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(string.Format("ThreadPool type '{0}' could not be instantiated.", tpType),
                                           e);
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
                initException =
                    new SchedulerException(
                        string.Format("ThreadPool type '{0}' props could not be configured.", tpType), e);
                initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                throw initException;
            }

                        // Set up any DataSources
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string[] dsNames = cfg.GetPropertyGroups(PROP_DATASOURCE_PREFIX);
            for (int i = 0; i < dsNames.Length; i++)
            {
                PropertiesParser pp =
                    new PropertiesParser(
                        cfg.GetPropertyGroup(string.Format("{0}.{1}", PROP_DATASOURCE_PREFIX, dsNames[i]), true));

                string cpType = pp.GetStringProperty(PROP_DB_PROVIDER_TYPE, null);

                // custom connectionProvider...
                if (cpType != null)
                {
                    IDbProvider cp;
                    try
                    {
                        cp = (IDbProvider) ObjectUtils.InstantiateType(loadHelper.LoadType(cpType));
                    }
                    catch (Exception e)
                    {
                        initException =
                            new SchedulerException(
                                string.Format("ConnectionProvider of type '{0}' could not be instantiated.", cpType), e);
                        initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                        throw initException;
                    }

                    try
                    {
                        // remove the type name, so it isn't attempted to be set
                        pp.UnderlyingProperties.Remove(PROP_DB_PROVIDER_TYPE);

                        ObjectUtils.SetObjectProperties(cp, pp.UnderlyingProperties);
                    }
                    catch (Exception e)
                    {
                        initException =
                            new SchedulerException(
                                string.Format("ConnectionProvider type '{0}' props could not be configured.", cpType),
                                e);
                        initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                        throw initException;
                    }

                    dbMgr = DBConnectionManager.Instance;
                    dbMgr.AddConnectionProvider(dsNames[i], cp);
                }
                else
                {
                    string dsProvider = pp.GetStringProperty(PROP_DATASOURCE_PROVIDER, null);
                    string dsConnectionString = pp.GetStringProperty(PROP_DATASOURCE_CONNECTION_STRING, null);

                    if (dsProvider == null)
                    {
                        initException =
                            new SchedulerException(string.Format("Provider not specified for DataSource: {0}", dsNames[i]));
                        throw initException;
                    }
                    if (dsConnectionString == null)
                    {
                        initException =
                            new SchedulerException(string.Format("Connection string not specified for DataSource: {0}", dsNames[i]));
                        throw initException;
                    }
                    try
                    {
                        DbProvider dbp = new DbProvider(dsProvider, dsConnectionString);
						
                        dbMgr = DBConnectionManager.Instance;
                        dbMgr.AddConnectionProvider(dsNames[i], dbp);
                    }
                    catch (Exception exception)
                    {
                        initException =
                            new SchedulerException(string.Format("Could not Initialize DataSource: {0}", dsNames[i]),
                                                   exception);
                        throw initException;
                    }
                }
            }

            // Get JobStore Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string jsType = cfg.GetStringProperty(PROP_JOB_STORE_TYPE, typeof (RAMJobStore).FullName);

            if (jsType == null)
            {
                initException =
                    new SchedulerException("JobStore type not specified. ", SchedulerException.ERR_BAD_CONFIGURATION);
                throw initException;
            }

            try
            {
                js = (IJobStore) ObjectUtils.InstantiateType(loadHelper.LoadType(jsType));
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(string.Format("JobStore of type '{0}' could not be instantiated.", jsType), e);
                initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                throw initException;
            }
            
            tProps =
                cfg.GetPropertyGroup(PROP_JOB_STORE_PREFIX, true, new string[] {PROP_JOB_STORE_LOCK_HANDLER_PREFIX});
            
            try
            {
                ObjectUtils.SetObjectProperties(js, tProps);
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(
                        string.Format("JobStore type '{0}' props could not be configured.", jsType), e);
                initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                throw initException;
            }


            if (js is JobStoreSupport)
            {
                ((JobStoreSupport) js).InstanceId = schedInstId;
                ((JobStoreSupport) js).InstanceName = schedName;

                // Install custom lock handler (Semaphore)
                string lockHandlerTypeName = cfg.GetStringProperty(PROP_JOB_STORE_LOCK_HANDLER_TYPE);
                if (lockHandlerTypeName != null)
                {
                    try
                    {
                        Type lockHandlerType = loadHelper.LoadType(lockHandlerTypeName);
                        ISemaphore lockHandler;
                        ConstructorInfo cWithDbProvider =
                            lockHandlerType.GetConstructor(new Type[] {typeof (DbProvider)});

                        if (cWithDbProvider != null)
                        {
                            // takes db provider
                            IDbProvider dbProvider = DBConnectionManager.Instance.GetDbProvider(((JobStoreSupport) js).DataSource);
                            lockHandler = (ISemaphore) cWithDbProvider.Invoke(new object[] { dbProvider });
                        }
                        else
                        {
                            lockHandler = (ISemaphore)ObjectUtils.InstantiateType(lockHandlerType);
                        }

                        tProps = cfg.GetPropertyGroup(PROP_JOB_STORE_LOCK_HANDLER_PREFIX, true);

                        // If this lock handler requires the table prefix, add it to its properties.
                        if (lockHandler is ITablePrefixAware)
                        {
                            tProps[PROP_TABLE_PREFIX] = ((JobStoreSupport) js).TablePrefix;
                        }

                        try
                        {
                            ObjectUtils.SetObjectProperties(lockHandler, tProps);
                        }
                        catch (Exception e)
                        {
                            initException = new SchedulerException(string.Format("JobStore LockHandler type '{0}' props could not be configured.", lockHandlerTypeName), e);
                            initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                            throw initException;
                        }

                        ((JobStoreSupport) js).LockHandler = lockHandler;
                        Log.Info("Using custom data access locking (synchronization): " + lockHandlerType);
                    }
                    catch (Exception e)
                    {
                        initException = new SchedulerException(string.Format("JobStore LockHandler type '{0}' could not be instantiated.", lockHandlerTypeName), e);
                        initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                        throw initException;
                    }
                }
            }

            // Set up any SchedulerPlugins
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string[] pluginNames = cfg.GetPropertyGroups(PROP_PLUGIN_PREFIX);
            ISchedulerPlugin[] plugins = new ISchedulerPlugin[pluginNames.Length];
            for (int i = 0; i < pluginNames.Length; i++)
            {
                NameValueCollection pp =
                    cfg.GetPropertyGroup(string.Format("{0}.{1}", PROP_PLUGIN_PREFIX, pluginNames[i]), true);

                string plugInType = pp[PROP_PLUGIN_TYPE] == null ? null : pp[PROP_PLUGIN_TYPE];

                if (plugInType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format("SchedulerPlugin type not specified for plugin '{0}'", pluginNames[i]),
                            SchedulerException.ERR_BAD_CONFIGURATION);
                    throw initException;
                }
                ISchedulerPlugin plugin;
                try
                {
                    plugin = (ISchedulerPlugin) ObjectUtils.InstantiateType(LoadType(plugInType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("SchedulerPlugin of type '{0}' could not be instantiated.", plugInType), e);
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
                        new SchedulerException(
                            string.Format("JobStore SchedulerPlugin '{0}' props could not be configured.", plugInType),
                            e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
                plugins[i] = plugin;
            }

            // Set up any JobListeners
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            Type[] strArg = new Type[] {typeof (string)};
            string[] jobListenerNames = cfg.GetPropertyGroups(PROP_JOB_LISTENER_PREFIX);
            IJobListener[] jobListeners = new IJobListener[jobListenerNames.Length];
            for (int i = 0; i < jobListenerNames.Length; i++)
            {
                NameValueCollection lp =
                    cfg.GetPropertyGroup(string.Format("{0}.{1}", PROP_JOB_LISTENER_PREFIX, jobListenerNames[i]), true);

                string listenerType = lp[PROP_LISTENER_TYPE] == null ? null : lp[PROP_LISTENER_TYPE];

                if (listenerType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format("JobListener type not specified for listener '{0}'", jobListenerNames[i]),
                            SchedulerException.ERR_BAD_CONFIGURATION);
                    throw initException;
                }
                IJobListener listener;
                try
                {
                    listener = (IJobListener) ObjectUtils.InstantiateType(loadHelper.LoadType(listenerType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("JobListener of type '{0}' could not be instantiated.", listenerType), e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
                try
                {
                    MethodInfo nameSetter =
                        listener.GetType().GetMethod("setName", (strArg == null) ? new Type[0] : strArg);
                    if (nameSetter != null)
                    {
                        nameSetter.Invoke(listener, new object[] {jobListenerNames[i]});
                    }
                    ObjectUtils.SetObjectProperties(listener, lp);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("JobListener '{0}' props could not be configured.", listenerType), e);
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
                NameValueCollection lp =
                    cfg.GetPropertyGroup(
                        string.Format("{0}.{1}", PROP_TRIGGER_LISTENER_PREFIX, triggerListenerNames[i]), true);

                string listenerType = lp[PROP_LISTENER_TYPE] == null ? null : lp[PROP_LISTENER_TYPE];

                if (listenerType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format("TriggerListener type not specified for listener '{0}'",
                                          triggerListenerNames[i]),
                            SchedulerException.ERR_BAD_CONFIGURATION);
                    throw initException;
                }
                ITriggerListener listener;
                try
                {
                    listener = (ITriggerListener) ObjectUtils.InstantiateType(loadHelper.LoadType(listenerType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("TriggerListener of type '{0}' could not be instantiated.", listenerType), e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
                try
                {
                    MethodInfo nameSetter =
                        listener.GetType().GetMethod("setName", (strArg == null) ? new Type[0] : strArg);
                    if (nameSetter != null)
                    {
                        nameSetter.Invoke(listener, (new object[] {triggerListenerNames[i]}));
                    }
                    ObjectUtils.SetObjectProperties(listener, lp);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format("TriggerListener '{0}' props could not be configured.", listenerType), e);
                    initException.ErrorCode = SchedulerException.ERR_BAD_CONFIGURATION;
                    throw initException;
                }
                triggerListeners[i] = listener;
            }


            // Fire everything up
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            IJobRunShellFactory jrsf = new StdJobRunShellFactory();
            
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
            // rsrcs.MakeSchedulerThreadDaemon = makeSchedulerThreadDaemon;
            rsrcs.ThreadPool = tp;
            if (tp is SimpleThreadPool)
            {
                ((SimpleThreadPool) tp).ThreadNamePrefix = schedName + "_Worker";
            }
            tp.Initialize();

            rsrcs.JobStore = js;

            // add plugins
            for (int i = 0; i < plugins.Length; i++)
            {
                rsrcs.AddSchedulerPlugin(plugins[i]);
            }


            schedCtxt = new SchedulingContext();
            schedCtxt.InstanceId = rsrcs.InstanceId;

            qs = new QuartzScheduler(rsrcs, schedCtxt, idleWaitTime, dbFailureRetry);

            // Create Scheduler ref...
            IScheduler sched = Instantiate(rsrcs, qs);

            // set job factory if specified
            if (jobFactory != null)
            {
                qs.JobFactory = jobFactory;
            }

            // Initialize plugins now that we have a Scheduler instance.
            for (int i = 0; i < plugins.Length; i++)
            {
                plugins[i].Initialize(pluginNames[i], sched);
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
                string key = (String) itr.Current;
                string val = schedCtxtProps.Get(key);

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


        protected virtual Type LoadType(string typeName)
        {
            return Type.GetType(typeName);
        }

        /// <summary>
        /// Returns a handle to the Scheduler produced by this factory.
        /// </summary>
        /// 
        /// <remarks>
        /// If one of the <see cref="Initialize()" /> methods has not be previously
        /// called, then the default (no-arg) <see cref="Initialize()" /> method
        /// will be called by this method.
        /// </remarks>
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
        public virtual IScheduler GetScheduler(string schedName)
        {
            return SchedulerRepository.Instance.Lookup(schedName);
        }
    }
}