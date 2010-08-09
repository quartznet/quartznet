#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Reflection;

using Common.Logging;

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
    /// based on the contents of a properties file.
    /// </summary>
    /// <remarks>
    /// <p>
    /// By default a properties are loaded from App.config's quartz section. 
    /// If that fails, then the "quartz.properties"
    /// file located (as a embedded resource) in Quartz.dll is loaded. If you
    /// wish to use a file other than these defaults, you must define the system
    /// property 'quartz.properties' to point to the file you want.
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
    /// myProp = 10' then after the JobStore class has been instantiated, the property
    /// 'MyProp' will be set with the value. Type conversion to primitive CLR types
    /// (int, long, float, double, boolean, enum and string) are performed before calling
    /// the property's setter method.
    /// </p>
    /// </remarks>
    /// <author>James House</author>
    /// <author>Anthony Eden</author>
    /// <author>Mohammad Rezaei</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdSchedulerFactory : ISchedulerFactory
    {
        private const string ConfigurationKeyPrefix = "quartz.";
        public const string PropertiesFile = "quartz.config";
        public const string PropertySchedulerInstanceName = "quartz.scheduler.instanceName";
        public const string PropertySchedulerInstanceId = "quartz.scheduler.instanceId";
        public const string PropertySchedulerInstanceIdGeneratorPrefix = "quartz.scheduler.instanceIdGenerator";
        public const string PropertySchedulerInstanceIdGeneratorType = PropertySchedulerInstanceIdGeneratorPrefix + ".type";
        public const string PropertySchedulerThreadName = "quartz.scheduler.threadName";
        public const string PropertySchedulerBatchTimeWindow = "quartz.scheduler.batchTimeWindow";
        public const string PropertySchedulerMaxBatchSize = "quartz.scheduler.maxBatchSize";
        public const string PropertySchedulerExporterPrefix = "quartz.scheduler.exporter";
        public const string PropertySchedulerExporterType = PropertySchedulerExporterPrefix + ".type";
        public const string PropertySchedulerProxy = "quartz.scheduler.proxy";
        public const string PropertySchedulerProxyAddress = "quartz.scheduler.proxy.address";
        public const string PropertySchedulerIdleWaitTime = "quartz.scheduler.idleWaitTime";
        public const string PropertySchedulerDbFailureRetryInterval = "quartz.scheduler.dbFailureRetryInterval";
        public const string PropertySchedulerMakeSchedulerThreadDaemon = "quartz.scheduler.makeSchedulerThreadDaemon";
        public const string PropertySchedulerTypeLoadHelperType = "quartz.scheduler.typeLoadHelper.type";
        public const string PropertySchedulerJobFactoryType = "quartz.scheduler.jobFactory.type";
        public const string PropertySchedulerJobFactoryPrefix = "quartz.scheduler.jobFactory";
        public const string PropertySchedulerInterruptJobsOnShutdown = "quartz.scheduler.interruptJobsOnShutdown";
        public const string PropertySchedulerInterruptJobsOnShutdownWithWait = "quartz.scheduler.interruptJobsOnShutdownWithWait";
        public const string PropertySchedulerContextPrefix = "quartz.context.key";
        public const string PropertyThreadPoolPrefix = "quartz.threadPool";
        public const string PropertyThreadPoolType = "quartz.threadPool.type";
        public const string PropertyJobStorePrefix = "quartz.jobStore";
        public const string PropertyJobStoreLockHandlerPrefix = PropertyJobStorePrefix + ".lockHandler";
        public const string PropertyJobStoreLockHandlerType = PropertyJobStoreLockHandlerPrefix + ".type";
        public const string PropertyTablePrefix = "tablePrefix";
        public const string PropertyJobStoreType = "quartz.jobStore.type";
        public const string PropertyJobStoreUseProperties = "quartz.jobStore.useProperties";
        public const string PropertyDataSourcePrefix = "quartz.dataSource";
        public const string PropertyDbProviderType = "connectionProvider.type";
        public const string PropertyDataSourceProvider = "provider";
        public const string PropertyDataSourceConnectionString = "connectionString";
        public const string PropertyDataSourceConnectionStringName = "connectionStringName";
        public const string PropertyDataSourceValidationQuery = "validationQuery";
        public const string PropertyPluginPrefix = "quartz.plugin";
        public const string PropertyPluginType = "type";
        public const string PropertyJobListenerPrefix = "quartz.jobListener";
        public const string PropertyTriggerListenerPrefix = "quartz.triggerListener";
        public const string PropertyListenerType = "type";
        public const string DefaultInstanceId = "NON_CLUSTERED";
        public const string PropertyCheckConfiguration = "quartz.checkConfiguration";
        public const string AutoGenerateInstanceId = "AUTO";
        private SchedulerException initException;

        private PropertiesParser cfg;

        private static readonly ILog log = LogManager.GetLogger(typeof (StdSchedulerFactory));

        private string SchedulerName
        {
            get { return cfg.GetStringProperty(PropertySchedulerInstanceName, "QuartzScheduler"); }
        }

        protected ILog Log
        {
            get { return log; }
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
        public virtual ICollection<IScheduler> AllSchedulers
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
        /// Initialize the <see cref="ISchedulerFactory" />.
        /// </summary>
        /// <remarks>
        /// By default a properties file named "quartz.properties" is loaded from
        /// the 'current working directory'. If that fails, then the
        /// "quartz.properties" file located (as an embedded resource) in the Quartz.NET
        /// assembly is loaded. If you wish to use a file other than these defaults,
        /// you must define the system property 'quartz.properties' to point to
        /// the file you want.
        /// </remarks>
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

            NameValueCollection props = (NameValueCollection) ConfigurationManager.GetSection("quartz");

            string requestedFile = Environment.GetEnvironmentVariable(PropertiesFile);
            string propFileName = requestedFile != null && requestedFile.Trim().Length > 0 ? requestedFile : "~/quartz.config";

            // check for specials
            propFileName = FileUtil.ResolveFile(propFileName);

            if (props == null && File.Exists(propFileName))
            {
                // file system
                try
                {
                    PropertiesParser pp = PropertiesParser.ReadFromFileResource(propFileName);
                    props = pp.UnderlyingProperties;
                    Log.Info(string.Format("Quartz.NET properties loaded from configuration file '{0}'", propFileName));
                }
                catch (Exception ex)
                {
                    Log.Error(
                        string.Format(CultureInfo.InvariantCulture, 
                        "Could not load properties for Quartz from file {0}: {1}",
                        propFileName,              
                        ex.Message), ex);
                }

            }
            if (props == null)
            {
                // read from assembly
                try
                {
                    PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource("Quartz.quartz.config");
                    props = pp.UnderlyingProperties;
                    Log.Info("Default Quartz.NET properties loaded from embedded resource file");
                }
                catch (Exception ex)
                {
                    Log.Error(
                        string.Format(CultureInfo.InvariantCulture, "Could not load default properties for Quartz from Quartz assembly: {0}",
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
        /// the contents of the given key value collection object.
        /// </summary>
        public virtual void Initialize(NameValueCollection props)
        {
            cfg = new PropertiesParser(props);
            ValidateConfiguration();
        }

        private void ValidateConfiguration()
        {
            if (!cfg.GetBooleanProperty(PropertyCheckConfiguration, true))
            {
                // should not validate
                return;
            }

            // determine currently supported configuration keys via reflection
            List<string> supportedKeys = new List<string>();
            List<FieldInfo> fields = new List<FieldInfo>(GetType().GetFields(BindingFlags.Static | BindingFlags.Public));
            // choose constant string fields
            fields = fields.FindAll(field => field.FieldType == typeof (string));

            // read value from each field
            foreach (FieldInfo field in fields)
            {
                string value = (string) field.GetValue(null);
                if (value != null && value.StartsWith(ConfigurationKeyPrefix))
                {
                    supportedKeys.Add(value);
                }
            }

            // now check against allowed))
            foreach (string configurationKey in cfg.UnderlyingProperties.AllKeys)
            {
                if (!configurationKey.StartsWith(ConfigurationKeyPrefix))
                {
                    // don't bother if truly unknown property
                    continue;
                }

                bool isMatch = false;
                foreach (string supportedKey in supportedKeys)
                {
                    if (configurationKey.StartsWith(supportedKey, StringComparison.InvariantCulture))
                    {
                        isMatch = true;
                        break;
                    }
                }
                if (!isMatch)
                {
                    throw new SchedulerConfigException("Unknown configuration property '" + configurationKey + "'");
                }
            }

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

            ISchedulerExporter exporter = null;
            IJobStore js;
            IThreadPool tp;
            QuartzScheduler qs = null;
            SchedulingContext schedCtxt;
            DBConnectionManager dbMgr = null;
            string instanceIdGeneratorType = null;
            NameValueCollection tProps;
            bool autoId = false;
            TimeSpan idleWaitTime = TimeSpan.Zero;
            TimeSpan dbFailureRetry = TimeSpan.Zero;
            string typeLoadHelperType;
            string jobFactoryType;

            SchedulerRepository schedRep = SchedulerRepository.Instance;

            // Get Scheduler Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string schedName = cfg.GetStringProperty(PropertySchedulerInstanceName, "QuartzScheduler");
            string threadName =
                cfg.GetStringProperty(PropertySchedulerThreadName, string.Format(CultureInfo.InvariantCulture, "{0}_QuartzSchedulerThread", schedName));
            string schedInstId = cfg.GetStringProperty(PropertySchedulerInstanceId, DefaultInstanceId);

            if (schedInstId.Equals(AutoGenerateInstanceId))
            {
                autoId = true;
                instanceIdGeneratorType =
                    cfg.GetStringProperty(PropertySchedulerInstanceIdGeneratorType,
                                          "Quartz.Simpl.SimpleInstanceIdGenerator, Quartz");
            }


            typeLoadHelperType =
                cfg.GetStringProperty(PropertySchedulerTypeLoadHelperType,
                                      "Quartz.Simpl.SimpleTypeLoadHelper, Quartz");
            jobFactoryType = cfg.GetStringProperty(PropertySchedulerJobFactoryType, null);

            idleWaitTime = cfg.GetTimeSpanProperty(PropertySchedulerIdleWaitTime, idleWaitTime);
            dbFailureRetry = cfg.GetTimeSpanProperty(PropertySchedulerDbFailureRetryInterval, dbFailureRetry);
            bool makeSchedulerThreadDaemon = cfg.GetBooleanProperty(PropertySchedulerMakeSchedulerThreadDaemon);
            long batchTimeWindow = cfg.GetLongProperty(PropertySchedulerBatchTimeWindow, 0L);
            int maxBatchSize = cfg.GetIntProperty(PropertySchedulerMaxBatchSize, 10);

            bool interruptJobsOnShutdown = cfg.GetBooleanProperty(PropertySchedulerInterruptJobsOnShutdown, false);
            bool interruptJobsOnShutdownWithWait = cfg.GetBooleanProperty(PropertySchedulerInterruptJobsOnShutdownWithWait, false);

            NameValueCollection schedCtxtProps = cfg.GetPropertyGroup(PropertySchedulerContextPrefix, true);

            bool proxyScheduler = cfg.GetBooleanProperty(PropertySchedulerProxy, false);
            // If Proxying to remote scheduler, short-circuit here...
            // ~~~~~~~~~~~~~~~~~~
            if (proxyScheduler)
            {
                if (autoId)
                {
                    schedInstId = DefaultInstanceId;
                }

                schedCtxt = new SchedulingContext();
                schedCtxt.InstanceId = schedInstId;

                string uid = QuartzSchedulerResources.GetUniqueIdentifier(schedName, schedInstId);

                RemoteScheduler remoteScheduler = new RemoteScheduler(schedCtxt, uid);
                string remoteSchedulerAddress = cfg.GetStringProperty(PropertySchedulerProxyAddress);
                remoteScheduler.RemoteSchedulerAddress = remoteSchedulerAddress;
                schedRep.Bind(remoteScheduler);

                return remoteScheduler;
            }

            // Create type load helper
            ITypeLoadHelper loadHelper;
            try
            {
                loadHelper = ObjectUtils.InstantiateType<ITypeLoadHelper>(LoadType(typeLoadHelperType));
            }
            catch (Exception e)
            {
                throw new SchedulerConfigException(
                    string.Format(CultureInfo.InvariantCulture, "Unable to instantiate type load helper: {0}", e.Message), e);
            }
            loadHelper.Initialize();

            IJobFactory jobFactory = null;
            if (jobFactoryType != null)
            {
                try
                {
                    jobFactory = ObjectUtils.InstantiateType<IJobFactory>(loadHelper.LoadType(jobFactoryType));
                }
                catch (Exception e)
                {
                    throw new SchedulerConfigException(
                        string.Format(CultureInfo.InvariantCulture, "Unable to Instantiate JobFactory: {0}", e.Message), e);
                }

                tProps = cfg.GetPropertyGroup(PropertySchedulerJobFactoryPrefix, true);
                try
                {
                    ObjectUtils.SetObjectProperties(jobFactory, tProps);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "JobFactory of type '{0}' props could not be configured.", jobFactoryType), e);
                    throw initException;
                }
            }

            IInstanceIdGenerator instanceIdGenerator = null;
            if (instanceIdGeneratorType != null)
            {
                try
                {
                    instanceIdGenerator = ObjectUtils.InstantiateType<IInstanceIdGenerator>(loadHelper.LoadType(instanceIdGeneratorType));
                }
                catch (Exception e)
                {
                    throw new SchedulerConfigException(
                        string.Format(CultureInfo.InvariantCulture, "Unable to Instantiate InstanceIdGenerator: {0}", e.Message), e);
                }
                tProps = cfg.GetPropertyGroup(PropertySchedulerInstanceIdGeneratorPrefix, true);
                try
                {
                    ObjectUtils.SetObjectProperties(instanceIdGenerator, tProps);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "InstanceIdGenerator of type '{0}' props could not be configured.",
                                          instanceIdGeneratorType), e);
                    throw initException;
                }
            }

            // Get ThreadPool Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string tpType = cfg.GetStringProperty(PropertyThreadPoolType, typeof(SimpleThreadPool).AssemblyQualifiedName);

            if (tpType == null)
            {
                initException = new SchedulerException("ThreadPool type not specified. ");
                throw initException;
            }

            try
            {
                tp = ObjectUtils.InstantiateType<IThreadPool>(loadHelper.LoadType(tpType));
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(string.Format(CultureInfo.InvariantCulture, "ThreadPool type '{0}' could not be instantiated.", tpType),
                                           e);
                throw initException;
            }
            tProps = cfg.GetPropertyGroup(PropertyThreadPoolPrefix, true);
            try
            {
                ObjectUtils.SetObjectProperties(tp, tProps);
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(
                        string.Format(CultureInfo.InvariantCulture, "ThreadPool type '{0}' props could not be configured.", tpType), e);
                throw initException;
            }

                        // Set up any DataSources
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string[] dsNames = cfg.GetPropertyGroups(PropertyDataSourcePrefix);
            for (int i = 0; i < dsNames.Length; i++)
            {
                PropertiesParser pp =
                    new PropertiesParser(
                        cfg.GetPropertyGroup(string.Format(CultureInfo.InvariantCulture, "{0}.{1}", PropertyDataSourcePrefix, dsNames[i]), true));

                string cpType = pp.GetStringProperty(PropertyDbProviderType, null);

                // custom connectionProvider...
                if (cpType != null)
                {
                    IDbProvider cp;
                    try
                    {
                        cp = ObjectUtils.InstantiateType<IDbProvider>(loadHelper.LoadType(cpType));
                    }
                    catch (Exception e)
                    {
                        initException =
                            new SchedulerException(
                                string.Format(CultureInfo.InvariantCulture, "ConnectionProvider of type '{0}' could not be instantiated.", cpType), e);
                        throw initException;
                    }

                    try
                    {
                        // remove the type name, so it isn't attempted to be set
                        pp.UnderlyingProperties.Remove(PropertyDbProviderType);

                        ObjectUtils.SetObjectProperties(cp, pp.UnderlyingProperties);
                    }
                    catch (Exception e)
                    {
                        initException =
                            new SchedulerException(
                                string.Format(CultureInfo.InvariantCulture, "ConnectionProvider type '{0}' props could not be configured.", cpType),
                                e);
                        throw initException;
                    }

                    dbMgr = DBConnectionManager.Instance;
                    dbMgr.AddConnectionProvider(dsNames[i], cp);
                }
                else
                {
                    string dsProvider = pp.GetStringProperty(PropertyDataSourceProvider, null);
                    string dsConnectionString = pp.GetStringProperty(PropertyDataSourceConnectionString, null);
                    string dsConnectionStringName = pp.GetStringProperty(PropertyDataSourceConnectionStringName, null);

                    if (dsConnectionString == null && !String.IsNullOrEmpty(dsConnectionStringName))
                    {

                        ConnectionStringSettings connectionStringSettings = ConfigurationManager.ConnectionStrings[dsConnectionStringName];
                        if (connectionStringSettings == null)
                        {
                            initException =
                                new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Named connection string '{0}' not found for DataSource: {1}", dsConnectionStringName,  dsNames[i]));
                            throw initException;
                        }
                        dsConnectionString = connectionStringSettings.ConnectionString;
                    }

                    if (dsProvider == null)
                    {
                        initException =
                            new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Provider not specified for DataSource: {0}", dsNames[i]));
                        throw initException;
                    }
                    if (dsConnectionString == null)
                    {
                        initException =
                            new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Connection string not specified for DataSource: {0}", dsNames[i]));
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
                            new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Could not Initialize DataSource: {0}", dsNames[i]),
                                                   exception);
                        throw initException;
                    }
                }
            }

            // Get JobStore Properties
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string jsType = cfg.GetStringProperty(PropertyJobStoreType, typeof (RAMJobStore).FullName);

            if (jsType == null)
            {
                initException = new SchedulerException("JobStore type not specified. ");
                throw initException;
            }

            try
            {
                js = ObjectUtils.InstantiateType<IJobStore>(loadHelper.LoadType(jsType));
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobStore of type '{0}' could not be instantiated.", jsType), e);
                throw initException;
            }


            SchedulerDetailsSetter.SetDetails(js, schedName, schedInstId);

            tProps =
                cfg.GetPropertyGroup(PropertyJobStorePrefix, true, new string[] {PropertyJobStoreLockHandlerPrefix});
            
            try
            {
                ObjectUtils.SetObjectProperties(js, tProps);
            }
            catch (Exception e)
            {
                initException =
                    new SchedulerException(
                        string.Format(CultureInfo.InvariantCulture, "JobStore type '{0}' props could not be configured.", jsType), e);
                throw initException;
            }

            if (js is JobStoreSupport)
            {
                // Install custom lock handler (Semaphore)
                string lockHandlerTypeName = cfg.GetStringProperty(PropertyJobStoreLockHandlerType);
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
                            lockHandler = ObjectUtils.InstantiateType<ISemaphore>(lockHandlerType);
                        }

                        tProps = cfg.GetPropertyGroup(PropertyJobStoreLockHandlerPrefix, true);

                        // If this lock handler requires the table prefix, add it to its properties.
                        if (lockHandler is ITablePrefixAware)
                        {
                            tProps[PropertyTablePrefix] = ((JobStoreSupport) js).TablePrefix;
                        }

                        try
                        {
                            ObjectUtils.SetObjectProperties(lockHandler, tProps);
                        }
                        catch (Exception e)
                        {
                            initException = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobStore LockHandler type '{0}' props could not be configured.", lockHandlerTypeName), e);
                            throw initException;
                        }

                        ((JobStoreSupport) js).LockHandler = lockHandler;
                        Log.Info("Using custom data access locking (synchronization): " + lockHandlerType);
                    }
                    catch (Exception e)
                    {
                        initException = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobStore LockHandler type '{0}' could not be instantiated.", lockHandlerTypeName), e);
                        throw initException;
                    }
                }
            }

            // Set up any SchedulerPlugins
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string[] pluginNames = cfg.GetPropertyGroups(PropertyPluginPrefix);
            ISchedulerPlugin[] plugins = new ISchedulerPlugin[pluginNames.Length];
            for (int i = 0; i < pluginNames.Length; i++)
            {
                NameValueCollection pp =
                    cfg.GetPropertyGroup(string.Format(CultureInfo.InvariantCulture, "{0}.{1}", PropertyPluginPrefix, pluginNames[i]), true);

                string plugInType = pp[PropertyPluginType] ?? null;

                if (plugInType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "SchedulerPlugin type not specified for plugin '{0}'", pluginNames[i]));
                    throw initException;
                }
                ISchedulerPlugin plugin;
                try
                {
                    plugin = ObjectUtils.InstantiateType<ISchedulerPlugin>(LoadType(plugInType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "SchedulerPlugin of type '{0}' could not be instantiated.", plugInType), e);
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
                            string.Format(CultureInfo.InvariantCulture, "JobStore SchedulerPlugin '{0}' props could not be configured.", plugInType),
                            e);
                    throw initException;
                }
                plugins[i] = plugin;
            }

            // Set up any JobListeners
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            Type[] strArg = new Type[] {typeof (string)};
            string[] jobListenerNames = cfg.GetPropertyGroups(PropertyJobListenerPrefix);
            IJobListener[] jobListeners = new IJobListener[jobListenerNames.Length];
            for (int i = 0; i < jobListenerNames.Length; i++)
            {
                NameValueCollection lp =
                    cfg.GetPropertyGroup(string.Format(CultureInfo.InvariantCulture, "{0}.{1}", PropertyJobListenerPrefix, jobListenerNames[i]), true);

                string listenerType = lp[PropertyListenerType];

                if (listenerType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "JobListener type not specified for listener '{0}'", jobListenerNames[i]));
                    throw initException;
                }
                IJobListener listener;
                try
                {
                    listener = ObjectUtils.InstantiateType<IJobListener>(loadHelper.LoadType(listenerType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "JobListener of type '{0}' could not be instantiated.", listenerType), e);
                    throw initException;
                }
                try
                {
                    MethodInfo nameSetter = listener.GetType().GetMethod("setName", strArg);
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
                            string.Format(CultureInfo.InvariantCulture, "JobListener '{0}' props could not be configured.", listenerType), e);
                    throw initException;
                }
                jobListeners[i] = listener;
            }

            // Set up any TriggerListeners
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string[] triggerListenerNames = cfg.GetPropertyGroups(PropertyTriggerListenerPrefix);
            ITriggerListener[] triggerListeners = new ITriggerListener[triggerListenerNames.Length];
            for (int i = 0; i < triggerListenerNames.Length; i++)
            {
                NameValueCollection lp =
                    cfg.GetPropertyGroup(
                        string.Format(CultureInfo.InvariantCulture, "{0}.{1}", PropertyTriggerListenerPrefix, triggerListenerNames[i]), true);

                string listenerType = lp[PropertyListenerType];

                if (listenerType == null)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "TriggerListener type not specified for listener '{0}'",
                                          triggerListenerNames[i]));
                    throw initException;
                }
                ITriggerListener listener;
                try
                {
                    listener = ObjectUtils.InstantiateType<ITriggerListener>(loadHelper.LoadType(listenerType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "TriggerListener of type '{0}' could not be instantiated.", listenerType), e);
                    throw initException;
                }
                try
                {
                    MethodInfo nameSetter = listener.GetType().GetMethod("setName", strArg);
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
                            string.Format(CultureInfo.InvariantCulture, "TriggerListener '{0}' props could not be configured.", listenerType), e);
                    throw initException;
                }
                triggerListeners[i] = listener;
            }

            // Get exporter
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            string exporterType = cfg.GetStringProperty(PropertySchedulerExporterType, null);

            if (exporterType != null)
            {
                try
                {
                    exporter = ObjectUtils.InstantiateType<ISchedulerExporter>(loadHelper.LoadType(exporterType));
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "Scheduler exporter of type '{0}' could not be instantiated.", exporterType), e);
                    throw initException;
                }

                tProps = cfg.GetPropertyGroup(PropertySchedulerExporterPrefix, true);

                try
                {
                    ObjectUtils.SetObjectProperties(exporter, tProps);
                }
                catch (Exception e)
                {
                    initException =
                        new SchedulerException(
                            string.Format(CultureInfo.InvariantCulture, "Scheduler exporter type '{0}' props could not be configured.", exporterType), e);
                    throw initException;
                }
            }

            bool tpInited = false;
            bool qsInited = false;

            // Fire everything up
            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

            try
            {
                IJobRunShellFactory jrsf = new StdJobRunShellFactory();
            
                if (autoId)
                {
                    try
                    {
                        schedInstId = DefaultInstanceId;

                        if (js.Clustered)
                        {
                            schedInstId = instanceIdGenerator.GenerateInstanceId();
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
                    jjs.DbRetryInterval = dbFailureRetry;
                }

                QuartzSchedulerResources rsrcs = new QuartzSchedulerResources();
                rsrcs.Name = schedName;
                rsrcs.ThreadName = threadName;
                rsrcs.InstanceId = schedInstId;
                rsrcs.JobRunShellFactory = jrsf;
                rsrcs.MakeSchedulerThreadDaemon = makeSchedulerThreadDaemon;
                rsrcs.BatchTimeWindow = TimeSpan.FromMilliseconds(batchTimeWindow);
                rsrcs.MaxBatchSize = maxBatchSize;
                rsrcs.InterruptJobsOnShutdown = interruptJobsOnShutdown;
                rsrcs.InterruptJobsOnShutdownWithWait = interruptJobsOnShutdownWithWait;
                rsrcs.SchedulerExporter = exporter;

                SchedulerDetailsSetter.SetDetails(tp, schedName, schedInstId);

                rsrcs.ThreadPool = tp;

                if (tp is SimpleThreadPool)
                {
                    ((SimpleThreadPool) tp).ThreadNamePrefix = schedName + "_Worker";
                }
                tp.Initialize();
                tpInited = true;

                rsrcs.JobStore = js;

                // add plugins
                for (int i = 0; i < plugins.Length; i++)
                {
                    rsrcs.AddSchedulerPlugin(plugins[i]);
                }

                schedCtxt = new SchedulingContext();
                schedCtxt.InstanceId = rsrcs.InstanceId;

                qs = new QuartzScheduler(rsrcs, schedCtxt, idleWaitTime, dbFailureRetry);
                qsInited = true;

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
                foreach (string key in schedCtxtProps)
                {
                    string val = schedCtxtProps.Get(key);
                    sched.Context.Put(key, val);
                }

                // fire up job store, and runshell factory

                js.InstanceId = schedInstId;
                js.InstanceName = schedName;
                js.Initialize(loadHelper, qs.SchedulerSignaler);

                jrsf.Initialize(sched, schedCtxt);

                Log.Info(string.Format(CultureInfo.InvariantCulture, "Quartz scheduler '{0}' initialized", sched.SchedulerName));

                Log.Info(string.Format(CultureInfo.InvariantCulture, "Quartz scheduler version: {0}", qs.Version));

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
            catch (SchedulerException)
            {
                if (qsInited)
                {
                    qs.Shutdown(false);
                }
                else if (tpInited)
                {
                    tp.Shutdown(false);
                }
                throw;
            }
            catch
            {
                if (qsInited)
                {
                    qs.Shutdown(false);
                }
                else if (tpInited)
                {
                    tp.Shutdown(false);
                }
                throw;
            }
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