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

using System.Collections.Specialized;
using System.Reflection;

using Microsoft.Extensions.Logging;

using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Diagnostics;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// An implementation of <see cref="ISchedulerFactory" /> that
/// does all of it's work of creating a <see cref="QuartzScheduler" /> instance
/// based on the contents of a properties file.
/// </summary>
/// <remarks>
/// <para>
/// By default a properties are loaded from App.config's quartz section.
/// If that fails, then the file is loaded "quartz.config". If file does not exist,
/// default configuration located (as a embedded resource) in Quartz.dll is loaded. If you
/// wish to use a file other than these defaults, you must define the system
/// property 'quartz.properties' to point to the file you want.
/// </para>
/// <para>
/// See the sample properties that are distributed with Quartz for
/// information about the various settings available within the file.
/// </para>
/// <para>
/// Alternatively, you can explicitly Initialize the factory by calling one of
/// the <see cref="Initialize()" /> methods before calling <see cref="GetScheduler(CancellationToken)" />.
/// </para>
/// <para>
/// Instances of the specified <see cref="IJobStore" />,
/// <see cref="IThreadPool" />, classes will be created
/// by name, and then any additional properties specified for them in the config
/// file will be set on the instance by calling an equivalent 'set' method. For
/// example if the properties file contains the property 'quartz.jobStore.
/// myProp = 10' then after the JobStore class has been instantiated, the property
/// 'MyProp' will be set with the value. Type conversion to primitive CLR types
/// (int, long, float, double, boolean, enum and string) are performed before calling
/// the property's setter method.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Anthony Eden</author>
/// <author>Mohammad Rezaei</author>
/// <author>Marko Lahma (.NET)</author>
public class StdSchedulerFactory : ISchedulerFactory
{
    private const string ConfigurationKeyPrefix = "quartz.";
    private const string ConfigurationKeyPrefixServer = "quartz.server";
    public const string PropertiesFile = "quartz.config";
    public const string PropertySchedulerInstanceName = "quartz.scheduler.instanceName";
    public const string PropertySchedulerInstanceId = "quartz.scheduler.instanceId";
    public const string PropertySchedulerInstanceIdGeneratorPrefix = "quartz.scheduler.instanceIdGenerator";
    public const string PropertySchedulerInstanceIdGeneratorType = PropertySchedulerInstanceIdGeneratorPrefix + ".type";
    public const string PropertySchedulerThreadName = "quartz.scheduler.threadName";
    public const string PropertySchedulerBatchTimeWindow = "quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow";
    public const string PropertySchedulerMaxBatchSize = "quartz.scheduler.batchTriggerAcquisitionMaxCount";
    public const string PropertySchedulerExporterPrefix = "quartz.scheduler.exporter";
    public const string PropertySchedulerExporterType = PropertySchedulerExporterPrefix + ".type";
    public const string PropertySchedulerProxy = "quartz.scheduler.proxy";
    public const string PropertySchedulerProxyType = "quartz.scheduler.proxy.type";
    public const string PropertySchedulerIdleWaitTime = "quartz.scheduler.idleWaitTime";
    public const string PropertySchedulerMakeSchedulerThreadDaemon = "quartz.scheduler.makeSchedulerThreadDaemon";
    public const string PropertySchedulerTypeLoadHelperType = "quartz.scheduler.typeLoadHelper.type";
    public const string PropertySchedulerJobFactoryType = "quartz.scheduler.jobFactory.type";
    public const string PropertySchedulerJobFactoryPrefix = "quartz.scheduler.jobFactory";
    public const string PropertySchedulerInterruptJobsOnShutdown = "quartz.scheduler.interruptJobsOnShutdown";
    public const string PropertySchedulerInterruptJobsOnShutdownWithWait = "quartz.scheduler.interruptJobsOnShutdownWithWait";
    public const string PropertySchedulerContextPrefix = "quartz.context.key";
    public const string PropertyThreadPoolPrefix = "quartz.threadPool";
    public const string PropertyThreadPoolType = "quartz.threadPool.type";
    public const string PropertyTimeProviderType = "quartz.timeProvider.type";
    public const string PropertyJobStoreDbRetryInterval = "quartz.jobStore.dbRetryInterval";
    public const string PropertyJobStorePrefix = "quartz.jobStore";
    public const string PropertyJobStoreLockHandlerPrefix = PropertyJobStorePrefix + ".lockHandler";
    public const string PropertyJobStoreLockHandlerType = PropertyJobStoreLockHandlerPrefix + ".type";
    public const string PropertyTablePrefix = "tablePrefix";
    public const string PropertySchedulerName = "schedName";
    public const string PropertyJobStoreType = "quartz.jobStore.type";
    public const string PropertyDataSourcePrefix = "quartz.dataSource";
    public const string PropertyDbProvider = "quartz.dbprovider";
    public const string PropertyDbProviderType = "connectionProvider.type";
    public const string PropertyDataSourceProvider = "provider";
    public const string PropertyDataSourceConnectionString = "connectionString";
    public const string PropertyDataSourceConnectionStringName = "connectionStringName";
    public const string PropertyPluginPrefix = "quartz.plugin";
    public const string PropertyPluginType = "type";
    public const string PropertyJobListenerPrefix = "quartz.jobListener";
    public const string PropertyTriggerListenerPrefix = "quartz.triggerListener";
    public const string PropertyListenerType = "type";
    public const string PropertyCheckConfiguration = "quartz.checkConfiguration";
    public const string PropertyThreadExecutor = "quartz.threadExecutor";
    public const string PropertyThreadExecutorType = "quartz.threadExecutor.type";
    public const string PropertyObjectSerializer = "quartz.serializer";

    // for validating configuration
    private static readonly string[] supportedKeys = [
        PropertySchedulerInstanceName,
        PropertySchedulerInstanceId,
        PropertySchedulerInstanceIdGeneratorPrefix,
        PropertySchedulerInstanceIdGeneratorType,
        PropertySchedulerThreadName,
        PropertySchedulerBatchTimeWindow,
        PropertySchedulerMaxBatchSize,
        PropertySchedulerExporterPrefix,
        PropertySchedulerExporterType,
        PropertySchedulerProxy,
        PropertySchedulerProxyType,
        PropertySchedulerIdleWaitTime,
        PropertySchedulerMakeSchedulerThreadDaemon,
        PropertySchedulerTypeLoadHelperType,
        PropertySchedulerJobFactoryType,
        PropertySchedulerJobFactoryPrefix,
        PropertySchedulerInterruptJobsOnShutdown,
        PropertySchedulerInterruptJobsOnShutdownWithWait,
        PropertySchedulerContextPrefix,
        PropertyThreadPoolPrefix,
        PropertyThreadPoolType,
        PropertyJobStoreDbRetryInterval,
        PropertyJobStorePrefix,
        PropertyJobStoreLockHandlerPrefix,
        PropertyJobStoreLockHandlerType,
        PropertyJobStoreType,
        PropertyDataSourcePrefix,
        PropertyDbProvider,
        PropertyDbProviderType,
        PropertyPluginPrefix,
        PropertyJobListenerPrefix,
        PropertyTriggerListenerPrefix,
        PropertyCheckConfiguration,
        PropertyThreadExecutor,
        PropertyThreadExecutorType,
        PropertyObjectSerializer,
        PropertyTimeProviderType,
    ];

    public const string DefaultInstanceId = "NON_CLUSTERED";
    public const string AutoGenerateInstanceId = "AUTO";
    public const string SystemPropertyAsInstanceId = "SYS_PROP";

    private readonly SemaphoreSlim semaphore = new(1, 1);

    private SchedulerException? initException;

    private PropertiesParser cfg = null!;

    internal ILogger<StdSchedulerFactory> logger;

    private string SchedulerName
    {
        // ReSharper disable once ArrangeAccessorOwnerBody
        get { return cfg.GetStringProperty(PropertySchedulerInstanceName, defaultValue: "QuartzScheduler")!; }
    }

    /// <summary>
    /// Returns a handle to the default Scheduler, creating it if it does not
    /// yet exist.
    /// </summary>
    /// <seealso cref="Initialize()">
    /// </seealso>
    public static ValueTask<IScheduler> GetDefaultScheduler(
        CancellationToken cancellationToken = default)
    {
        StdSchedulerFactory fact = new StdSchedulerFactory();
        return fact.GetScheduler(cancellationToken);
    }

    /// <summary> <para>
    /// Returns a handle to all known Schedulers (made by any
    /// StdSchedulerFactory instance.).
    /// </para>
    /// </summary>
    public virtual ValueTask<IReadOnlyList<IScheduler>> GetAllSchedulers(
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<IReadOnlyList<IScheduler>>(GetSchedulerRepository().LookupAll());
    }

    protected virtual ISchedulerRepository GetSchedulerRepository()
    {
        return SchedulerRepository.Instance;
    }

    protected virtual IDbConnectionManager GetDbConnectionManager()
    {
        return DBConnectionManager.Instance;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdSchedulerFactory"/> class.
    /// </summary>
    public StdSchedulerFactory()
    {
        this.logger = LogProvider.CreateLogger<StdSchedulerFactory>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdSchedulerFactory"/> class.
    /// </summary>
    /// <param name="props">The props.</param>
    public StdSchedulerFactory(NameValueCollection props) : this()
    {
        Initialize(props);
    }

    /// <summary>
    /// Initialize the <see cref="ISchedulerFactory" />.
    /// </summary>
    /// <remarks>
    /// By default a properties file named "quartz.config" is loaded from
    /// the 'current working directory'. If that fails, then the
    /// "quartz.config" file located (as an embedded resource) in the Quartz.NET
    /// assembly is loaded. If you wish to use a file other than these defaults,
    /// you must define the system property 'quartz.properties' to point to
    /// the file you want.
    /// </remarks>
    public virtual void Initialize()
    {
        // short-circuit if already initialized
        if (cfg is not null)
        {
            return;
        }
        if (initException is not null)
        {
            throw initException;
        }

        logger = LogProvider.CreateLogger<StdSchedulerFactory>();
        var props = InitializeProperties(logger, throwOnProblem: true);
        Initialize(OverrideWithSysProps(props ?? new NameValueCollection()));
    }

    internal static NameValueCollection? InitializeProperties(ILogger<StdSchedulerFactory> logger, bool throwOnProblem)
    {
        NameValueCollection? props = null;

        string? requestedFile = QuartzEnvironment.GetEnvironmentVariable(PropertiesFile);
        string propFileName = (requestedFile is not null && !string.IsNullOrWhiteSpace(requestedFile)) ? requestedFile : "~/quartz.config";

        // check for specials
        propFileName = FileUtil.ResolveFile(propFileName) ?? "quartz.config";

        if (File.Exists(propFileName))
        {
            // file system
            try
            {
                PropertiesParser pp = PropertiesParser.ReadFromFileResource(propFileName);
                props = pp.UnderlyingProperties;
                logger.LogInformation("Quartz.NET properties loaded from configuration file {PropFileName}", propFileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not load properties for Quartz from file {PropFileName}: {ExceptionMessage}", propFileName, ex.Message);
            }
        }

        if (props is null)
        {
            // read from assembly
            try
            {
                PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource("Quartz.quartz.config");
                props = pp.UnderlyingProperties;
                logger.LogInformation("Default Quartz.NET properties loaded from embedded resource file");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not load default properties for Quartz from Quartz assembly: {Message}", args: ex.Message);
            }
        }

        if (props is null && throwOnProblem)
        {
            ThrowHelper.ThrowSchedulerConfigException(
                @"Could not find <quartz> configuration section from your application config or load default configuration from assembly.
Please add configuration to your application config file to correctly initialize Quartz.");
        }


        return props;
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
        var vars = QuartzEnvironment.GetEnvironmentVariables();

        foreach (string key in vars.Keys)
        {
            // skip environment variable "quartz.config" that specifies the pros file,
            // because it looks like part of the quartz props, but is not, so it would make ValidateConfiguration fail
            if (string.Equals(key, PropertiesFile, StringComparison.Ordinal))
            {
                continue;
            }

            retValue.Set(key, vars[key]);
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
        Meters.Configure();
        ValidateConfiguration();
    }

    protected virtual void ValidateConfiguration()
    {
        if (!cfg.GetBooleanProperty(PropertyCheckConfiguration, true))
        {
            // should not validate
            return;
        }

        // now check against allowed
        foreach (var configurationKey in cfg.UnderlyingProperties.AllKeys)
        {
            if (configurationKey is null
                || !configurationKey.StartsWith(ConfigurationKeyPrefix)
                || configurationKey.StartsWith(ConfigurationKeyPrefixServer))
            {
                // don't bother if truly unknown property
                continue;
            }

            if (!IsSupportedConfigurationKey(configurationKey))
            {
                ThrowHelper.ThrowSchedulerConfigException($"Unknown configuration property '{configurationKey}'");
            }
        }
    }

    protected virtual bool IsSupportedConfigurationKey(string configurationKey)
    {
        foreach (var supportedKey in supportedKeys)
        {
            if (configurationKey.StartsWith(supportedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>  </summary>
    private async ValueTask<IScheduler> Instantiate()
    {
        if (cfg is null)
        {
            Initialize();
        }

        if (initException is not null)
        {
            throw initException;
        }

        TimeProvider timeProvider = TimeProvider.System;
        IJobStore js;
        IThreadPool tp;
        QuartzScheduler? qs = null;
        IDbConnectionManager? dbMgr = null;
        Type? instanceIdGeneratorType = null;
        NameValueCollection tProps;
        bool autoId = false;
        TimeSpan idleWaitTime = cfg!.GetTimeSpanProperty(PropertySchedulerIdleWaitTime, QuartzSchedulerResources.DefaultIdleWaitTime);
        TimeSpan dbFailureRetry = TimeSpan.FromSeconds(15);

        // Get Scheduler Properties
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        string schedName = cfg.GetStringProperty(PropertySchedulerInstanceName, "QuartzScheduler")!;
        string threadName = cfg.GetStringProperty(PropertySchedulerThreadName, $"{schedName}_QuartzSchedulerThread")!;
        var schedInstId = cfg.GetStringProperty(PropertySchedulerInstanceId, DefaultInstanceId)!;

        // Create type load helper
        Type? typeLoadHelperType = LoadType(cfg.GetStringProperty(PropertySchedulerTypeLoadHelperType));
        ITypeLoadHelper loadHelper;
        try
        {
            loadHelper = InstantiateType<ITypeLoadHelper>(typeLoadHelperType ?? typeof(SimpleTypeLoadHelper));
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowSchedulerConfigException($"Unable to instantiate type load helper: {e.Message}", e);
            return default;
        }

        loadHelper.Initialize();

        string? timeProviderTypeString = cfg.GetStringProperty(PropertyTimeProviderType);
        if (!string.IsNullOrWhiteSpace(timeProviderTypeString))
        {
            var timeProviderType = loadHelper.LoadType(timeProviderTypeString);
            if (timeProviderType is null)
            {
                logger.LogError("Unable to load time provider type: {TimeProviderType}", timeProviderTypeString);
            }
            else
            {
                timeProvider = InstantiateType<TimeProvider>(timeProviderType);
                logger.LogInformation("Using custom time provider: {TimeProviderType}", timeProviderTypeString);
            }
        }
        else
        {
            // try to resolve from DI, if possible
            try
            {
                timeProvider = InstantiateType<TimeProvider>(implementationType: null);
            }
            catch
            {
                // ignore and default to system
            }
        }

        if (schedInstId == AutoGenerateInstanceId)
        {
            autoId = true;
            instanceIdGeneratorType = loadHelper.LoadType(cfg.GetStringProperty(PropertySchedulerInstanceIdGeneratorType)) ?? typeof(SimpleInstanceIdGenerator);
        }
        else if (schedInstId == SystemPropertyAsInstanceId)
        {
            autoId = true;
            instanceIdGeneratorType = typeof(SystemPropertyInstanceIdGenerator);
        }

        dbFailureRetry = cfg.GetTimeSpanProperty(PropertyJobStoreDbRetryInterval, dbFailureRetry);
        if (dbFailureRetry < TimeSpan.Zero)
        {
            ThrowHelper.ThrowSchedulerException(PropertyJobStoreDbRetryInterval + " of less than 0 ms is not legal.");
        }

        bool makeSchedulerThreadDaemon = cfg.GetBooleanProperty(PropertySchedulerMakeSchedulerThreadDaemon);
        long batchTimeWindow = cfg.GetLongProperty(PropertySchedulerBatchTimeWindow, 0L);
        int maxBatchSize = cfg.GetIntProperty(PropertySchedulerMaxBatchSize, QuartzSchedulerResources.DefaultMaxBatchSize);

        bool interruptJobsOnShutdown = cfg.GetBooleanProperty(PropertySchedulerInterruptJobsOnShutdown, false);
        bool interruptJobsOnShutdownWithWait = cfg.GetBooleanProperty(PropertySchedulerInterruptJobsOnShutdownWithWait, false);

        var schedCtxtProps = cfg.GetPropertyGroup(PropertySchedulerContextPrefix, true);
        var proxyScheduler = cfg.GetBooleanProperty(PropertySchedulerProxy, false);

        // If Proxying to remote scheduler, short-circuit here...
        // ~~~~~~~~~~~~~~~~~~
        if (proxyScheduler)
        {
            if (autoId)
            {
                schedInstId = DefaultInstanceId;
            }

            var proxyType = loadHelper.LoadType(cfg.GetStringProperty(PropertySchedulerProxyType));
            IRemotableSchedulerProxyFactory factory;
            try
            {
                factory = InstantiateType<IRemotableSchedulerProxyFactory>(proxyType);
                ObjectUtils.SetObjectProperties(factory, cfg.GetPropertyGroup(PropertySchedulerProxy, true));
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"Remotable proxy factory '{proxyType}' could not be instantiated.", e);
                throw initException;
            }

            var remoteScheduler = factory.GetProxy(schedName, schedInstId);
            return remoteScheduler;
        }

        Type? jobFactoryType = loadHelper.LoadType(cfg.GetStringProperty(PropertySchedulerJobFactoryType));
        IJobFactory? jobFactory = null;
        if (jobFactoryType is not null)
        {
            try
            {
                jobFactory = InstantiateType<IJobFactory>(jobFactoryType);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowSchedulerConfigException($"Unable to Instantiate JobFactory: {e.Message}", e);
            }

            tProps = cfg.GetPropertyGroup(PropertySchedulerJobFactoryPrefix, stripPrefix: true);
            try
            {
                ObjectUtils.SetObjectProperties(jobFactory, tProps);
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"JobFactory of type '{jobFactoryType}' props could not be configured.", e);
                throw initException;
            }
        }

        IInstanceIdGenerator? instanceIdGenerator = null;
        if (instanceIdGeneratorType is not null)
        {
            try
            {
                instanceIdGenerator = InstantiateType<IInstanceIdGenerator>(instanceIdGeneratorType);
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowSchedulerConfigException($"Unable to Instantiate InstanceIdGenerator: {e.Message}", e);
            }
            tProps = cfg.GetPropertyGroup(PropertySchedulerInstanceIdGeneratorPrefix, stripPrefix: true);
            try
            {
                ObjectUtils.SetObjectProperties(instanceIdGenerator, tProps);
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"InstanceIdGenerator of type '{instanceIdGeneratorType}' props could not be configured.", e);
                throw initException;
            }
        }

        // Get ThreadPool Properties
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        var threadPoolTypeString = cfg.GetStringProperty(PropertyThreadPoolType).NullSafeTrim();
        if (threadPoolTypeString is not null
            && threadPoolTypeString.StartsWith("Quartz.Simpl.SimpleThreadPool", StringComparison.OrdinalIgnoreCase))
        {
            // default to use as synonym for now
            threadPoolTypeString = typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion();
        }

        Type tpType = loadHelper.LoadType(threadPoolTypeString) ?? typeof(DefaultThreadPool);

        try
        {
            tp = InstantiateType<IThreadPool>(tpType);
        }
        catch (Exception e)
        {
            initException = new SchedulerException($"ThreadPool type '{tpType}' could not be instantiated.", e);
            throw initException;
        }
        tProps = cfg.GetPropertyGroup(PropertyThreadPoolPrefix, stripPrefix: true);
        try
        {
            ObjectUtils.SetObjectProperties(tp, tProps);
        }
        catch (Exception e)
        {
            initException = new SchedulerException($"ThreadPool type '{tpType}' props could not be configured.", e);
            throw initException;
        }

        // Set up any DataSources
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        var dsNames = cfg.GetPropertyGroups(PropertyDataSourcePrefix);
        foreach (string dataSourceName in dsNames)
        {
            string datasourceKey = $"{PropertyDataSourcePrefix}.{dataSourceName}";
            NameValueCollection propertyGroup = cfg.GetPropertyGroup(datasourceKey, stripPrefix: true);
            PropertiesParser pp = new PropertiesParser(propertyGroup);

            Type? cpType = loadHelper.LoadType(pp.GetStringProperty(PropertyDbProviderType, defaultValue: null));

            // custom connectionProvider...
            if (cpType is not null)
            {
                IDbProvider cp;
                try
                {
                    cp = InstantiateType<IDbProvider>(cpType);
                }
                catch (Exception e)
                {
                    initException = new SchedulerException($"ConnectionProvider of type '{cpType}' could not be instantiated.", e);
                    throw initException;
                }

                try
                {
                    // get new grouping for connection provider
                    var group = datasourceKey + "." + "connectionProvider";
                    var dbProviderProperties = new PropertiesParser(cfg.GetPropertyGroup(group, stripPrefix: true));
                    // remove the type name, so it isn't attempted to be set
                    dbProviderProperties.UnderlyingProperties.Remove("type");

                    ObjectUtils.SetObjectProperties(cp, dbProviderProperties.UnderlyingProperties);
                    cp.Initialize();
                }
                catch (Exception e)
                {
                    initException = new SchedulerException($"ConnectionProvider type '{cpType}' props could not be configured.", e);
                    throw initException;
                }

                dbMgr = GetDbConnectionManager();
                dbMgr.AddConnectionProvider(dataSourceName, cp);
            }
            else
            {
                var dsProvider = pp.GetStringProperty(PropertyDataSourceProvider, defaultValue: null);
                var dsConnectionString = pp.GetStringProperty(PropertyDataSourceConnectionString, defaultValue: null);
                var dsConnectionStringName = pp.GetStringProperty(PropertyDataSourceConnectionStringName, defaultValue: null);

                if (dsConnectionString is null && !string.IsNullOrEmpty(dsConnectionStringName) && dsConnectionStringName is not null)
                {
                    var connectionString = GetNamedConnectionString(dsConnectionStringName);
                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        initException = new SchedulerException($"Named connection string '{dsConnectionStringName}' not found for DataSource: {dataSourceName}");
                        throw initException;
                    }
                    dsConnectionString = connectionString;
                }

                if (dsProvider is null)
                {
                    initException = new SchedulerException($"Provider not specified for DataSource: {dataSourceName}");
                    throw initException;
                }
                if (dsConnectionString is null)
                {
                    initException = new SchedulerException($"Connection string not specified for DataSource: {dataSourceName}");
                    throw initException;
                }
                try
                {
                    DbProvider dbp = new DbProvider(dsProvider, dsConnectionString);
                    dbp.Initialize();

                    dbMgr = GetDbConnectionManager();
                    dbMgr.AddConnectionProvider(dataSourceName, dbp);
                }
                catch (Exception exception)
                {
                    initException = new SchedulerException($"Could not Initialize DataSource: {dataSourceName}", exception);
                    throw initException;
                }
            }
        }

        // Get JobStore Properties
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        Type? jsType = loadHelper.LoadType(cfg.GetStringProperty(PropertyJobStoreType));
        try
        {
            js = InstantiateType<IJobStore>(jsType ?? typeof(RAMJobStore));
        }
        catch (Exception e)
        {
            initException = new SchedulerException($"JobStore of type '{jsType}' could not be instantiated.", e);
            throw initException;
        }

        // Get object serializer properties
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        IObjectSerializer? objectSerializer = null;
        string serializerTypeKey = "quartz.serializer.type";
        string? objectSerializerType = cfg.GetStringProperty(serializerTypeKey);
        if (objectSerializerType is not null)
        {
            // some aliases
            if (objectSerializerType.Equals("newtonsoft", StringComparison.OrdinalIgnoreCase))
            {
                objectSerializerType = "Quartz.Simpl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft";
            }
            if (objectSerializerType.Equals("stj", StringComparison.OrdinalIgnoreCase))
            {
                objectSerializerType = typeof(SystemTextJsonObjectSerializer).AssemblyQualifiedNameWithoutVersion();
            }
            if (objectSerializerType.Equals("binary", StringComparison.OrdinalIgnoreCase))
            {
                throw new SchedulerException("Binary serialization is not supported anymore. Use JSON serialization instead. You can also manually configure custom serializer.");
            }

            tProps = cfg.GetPropertyGroup(PropertyObjectSerializer, stripPrefix: true);
            try
            {
                objectSerializer = InstantiateType<IObjectSerializer>(loadHelper.LoadType(objectSerializerType));
                logger.LogInformation("Using object serializer: {Type}", objectSerializerType);

                ObjectUtils.SetObjectProperties(objectSerializer, tProps);

                objectSerializer.Initialize();
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"Object serializer type '{objectSerializerType}' could not be instantiated.", e);
                throw initException;
            }
        }
        else if (js.GetType() != typeof(RAMJobStore))
        {
            // when we know for sure that job store does not need serialization we can be a bit more relaxed
            // otherwise it's an error to not define the serialization strategy
            initException = new SchedulerException($"You must define object serializer using configuration key '{serializerTypeKey}' when using other than RAMJobStore. " +
                                                   "Out of the box supported values are 'json' and 'binary'. JSON doesn't suffer from versioning as much as binary serialization but you cannot use it if you already have binary serialized data.");
            throw initException;
        }
        js.InstanceName = schedName;
        js.InstanceId = schedInstId;

        tProps = cfg.GetPropertyGroup(PropertyJobStorePrefix, stripPrefix: true, excludedPrefixes: [PropertyJobStoreLockHandlerPrefix]);

        try
        {
            ObjectUtils.SetObjectProperties(js, tProps);
        }
        catch (Exception e)
        {
            initException = new SchedulerException($"JobStore type '{jsType}' props could not be configured.", e);
            throw initException;
        }

        if (js is JobStoreSupport jobStoreSupport)
        {
            // check if we have custom DI setup
            jobStoreSupport.ConnectionManager = GetDbConnectionManager();

            // Install custom lock handler (Semaphore)
            var lockHandlerType = loadHelper.LoadType(cfg.GetStringProperty(PropertyJobStoreLockHandlerType));
            if (lockHandlerType is not null)
            {
                try
                {
                    ISemaphore lockHandler;
                    var cWithDbProvider = lockHandlerType.GetConstructor([typeof(DbProvider)]);

                    if (cWithDbProvider is not null)
                    {
                        // takes db provider
                        IDbProvider dbProvider = GetDbConnectionManager().GetDbProvider(jobStoreSupport.DataSource);
                        lockHandler = (ISemaphore) cWithDbProvider.Invoke([dbProvider]);
                    }
                    else
                    {
                        lockHandler = InstantiateType<ISemaphore>(lockHandlerType);
                    }

                    tProps = cfg.GetPropertyGroup(PropertyJobStoreLockHandlerPrefix, stripPrefix: true);

                    // If this lock handler requires the table prefix, add it to its properties.
                    if (lockHandler is ITablePrefixAware)
                    {
                        tProps[PropertyTablePrefix] = jobStoreSupport.TablePrefix;
                        tProps[PropertySchedulerName] = schedName;
                    }

                    try
                    {
                        ObjectUtils.SetObjectProperties(lockHandler, tProps);
                    }
                    catch (Exception e)
                    {
                        initException = new SchedulerException($"JobStore LockHandler type '{lockHandlerType}' props could not be configured.", e);
                        throw initException;
                    }

                    jobStoreSupport.LockHandler = lockHandler;
                    logger.LogInformation("Using custom data access locking (synchronization): {LockHandlerType}", lockHandlerType);
                }
                catch (Exception e)
                {
                    initException = new SchedulerException($"JobStore LockHandler type '{lockHandlerType}' could not be instantiated.", e);
                    throw initException;
                }
            }
        }

        // Set up any SchedulerPlugins
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        var pluginNames = cfg.GetPropertyGroups(PropertyPluginPrefix);
        ISchedulerPlugin[] plugins = new ISchedulerPlugin[pluginNames.Count];
        for (int i = 0; i < pluginNames.Count; i++)
        {
            var pp = cfg.GetPropertyGroup($"{PropertyPluginPrefix}.{pluginNames[index: i]}", stripPrefix: true);
            var plugInType = pp[PropertyPluginType];

            if (plugInType is null)
            {
                initException = new SchedulerException($"SchedulerPlugin type not specified for plugin '{pluginNames[index: i]}'");
                throw initException;
            }
            ISchedulerPlugin plugin;
            try
            {
                var pluginTypeType = loadHelper.LoadType(plugInType) ?? throw new SchedulerException($"Could not load plugin type {plugInType}");
                // we need to use concrete types to resolve correct one
                var method = GetType().GetMethod(nameof(InstantiateType), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(pluginTypeType);
                plugin = (ISchedulerPlugin) method.Invoke(this, [pluginTypeType])!;
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"SchedulerPlugin of type '{plugInType}' could not be instantiated.", e);
                throw initException;
            }
            try
            {
                ObjectUtils.SetObjectProperties(plugin, pp);
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"JobStore SchedulerPlugin '{plugInType}' props could not be configured.", e);
                throw initException;
            }

            plugins[i] = plugin;
        }

        // Set up any JobListeners
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        var jobListenerNames = cfg.GetPropertyGroups(PropertyJobListenerPrefix);
        IJobListener[] jobListeners = new IJobListener[jobListenerNames.Count];
        for (int i = 0; i < jobListenerNames.Count; i++)
        {
            var lp = cfg.GetPropertyGroup(prefix: $"{PropertyJobListenerPrefix}.{jobListenerNames[index: i]}", stripPrefix: true);
            var listenerType = lp[PropertyListenerType];

            if (listenerType is null)
            {
                initException = new SchedulerException($"JobListener type not specified for listener '{jobListenerNames[index: i]}'");
                throw initException;
            }
            IJobListener listener;
            try
            {
                listener = InstantiateType<IJobListener>(loadHelper.LoadType(listenerType));
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"JobListener of type '{listenerType}' could not be instantiated.", e);
                throw initException;
            }
            try
            {
                var nameProperty = listener.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                if (nameProperty is not null && nameProperty.CanWrite)
                {
                    nameProperty.GetSetMethod()!.Invoke(listener, [jobListenerNames[index: i]]);
                }
                ObjectUtils.SetObjectProperties(listener, lp);
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"JobListener '{listenerType}' props could not be configured.", e);
                throw initException;
            }
            jobListeners[i] = listener;
        }

        // Set up any TriggerListeners
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        var triggerListenerNames = cfg.GetPropertyGroups(PropertyTriggerListenerPrefix);
        ITriggerListener[] triggerListeners = new ITriggerListener[triggerListenerNames.Count];
        for (int i = 0; i < triggerListenerNames.Count; i++)
        {
            var lp = cfg.GetPropertyGroup(prefix: $"{PropertyTriggerListenerPrefix}.{triggerListenerNames[index: i]}", stripPrefix: true);
            var listenerType = lp[PropertyListenerType];

            if (listenerType is null)
            {
                initException = new SchedulerException($"TriggerListener type not specified for listener '{triggerListenerNames[index: i]}'");
                throw initException;
            }
            ITriggerListener listener;
            try
            {
                listener = InstantiateType<ITriggerListener>(loadHelper.LoadType(listenerType));
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"TriggerListener of type '{listenerType}' could not be instantiated.", e);
                throw initException;
            }
            try
            {
                var nameProperty = listener.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                if (nameProperty is not null && nameProperty.CanWrite)
                {
                    nameProperty.GetSetMethod()!.Invoke(listener, [triggerListenerNames[index: i]]);
                }
                ObjectUtils.SetObjectProperties(listener, lp);
            }
            catch (Exception e)
            {
                initException = new SchedulerException($"TriggerListener '{listenerType}' props could not be configured.", e);
                throw initException;
            }
            triggerListeners[i] = listener;
        }

        bool tpInited = false;
        bool qsInited = false;

        // Fire everything up
        // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

        try
        {
            var jrsf = new StdJobRunShellFactory();

            if (autoId)
            {
                try
                {
                    schedInstId = DefaultInstanceId;

                    if (js.Clustered)
                    {
                        schedInstId = (await instanceIdGenerator!.GenerateInstanceId().ConfigureAwait(false))!;
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Couldn't generate instance Id!");
                    ThrowHelper.ThrowInvalidOperationException("Cannot run without an instance id.");
                }
            }

            if (js is JobStoreSupport js2)
            {
                js2.DbRetryInterval = dbFailureRetry;
                js2.ObjectSerializer = objectSerializer;
            }

            QuartzSchedulerResources rsrcs = new QuartzSchedulerResources();
            rsrcs.Name = schedName;
            rsrcs.ThreadName = threadName;
            rsrcs.InstanceId = schedInstId;
            rsrcs.JobRunShellFactory = jrsf;
            rsrcs.MakeSchedulerThreadDaemon = makeSchedulerThreadDaemon;
            rsrcs.IdleWaitTime = idleWaitTime;
            rsrcs.BatchTimeWindow = TimeSpan.FromMilliseconds(batchTimeWindow);
            rsrcs.MaxBatchSize = maxBatchSize;
            rsrcs.InterruptJobsOnShutdown = interruptJobsOnShutdown;
            rsrcs.InterruptJobsOnShutdownWithWait = interruptJobsOnShutdownWithWait;
            rsrcs.TimeProvider = timeProvider;
            rsrcs.SchedulerRepository = GetSchedulerRepository();

            tp.InstanceName = schedName;
            tp.InstanceId = schedInstId;

            rsrcs.ThreadPool = tp;

            tp.Initialize();
            tpInited = true;

            rsrcs.JobStore = js;

            // add plugins
            foreach (ISchedulerPlugin plugin in plugins)
            {
                rsrcs.AddSchedulerPlugin(plugin);
            }

            qs = new QuartzScheduler(rsrcs);
            qsInited = true;

            // Create Scheduler ref...
            IScheduler sched = Instantiate(rsrcs, qs);

            // set job factory if specified
            if (jobFactory is not null)
            {
                qs.JobFactory = jobFactory;
            }

            // Initialize plugins now that we have a Scheduler instance.
            for (int i = 0; i < plugins.Length; i++)
            {
                await plugins[i].Initialize(pluginNames[i], sched).ConfigureAwait(false);
            }

            // add listeners
            foreach (IJobListener listener in jobListeners)
            {
                qs.ListenerManager.AddJobListener(listener, EverythingMatcher<JobKey>.AllJobs());
            }
            foreach (ITriggerListener listener in triggerListeners)
            {
                qs.ListenerManager.AddTriggerListener(listener, EverythingMatcher<TriggerKey>.AllTriggers());
            }

            // set scheduler context data...
            foreach (var key in schedCtxtProps)
            {
                var val = schedCtxtProps.Get((string) key!);
                sched.Context.Put((string) key!, val);
            }

            // fire up job store, and job run shell factory

            js.InstanceId = schedInstId;
            js.InstanceName = schedName;
            js.ThreadPoolSize = tp.PoolSize;
            js.TimeProvider = timeProvider;
            await js.Initialize(loadHelper, qs.SchedulerSignaler).ConfigureAwait(false);

            jrsf.Initialize(sched);
            qs.Initialize();

            logger.LogInformation("Quartz Scheduler {Version} - '{SchedulerName}' with instanceId '{SchedulerInstanceId}' initialized", qs.Version, qs.SchedulerName, qs.SchedulerInstanceId);
            logger.LogInformation("Using thread pool '{ThreadPoolType}', size: {ThreadPoolSize}", qs.ThreadPoolClass.FullName, qs.ThreadPoolSize);
            logger.LogInformation("Using job store '{JobStoreType}', supports persistence: {SupportsPersistence}, clustered: {Clustered}", qs.JobStoreClass.FullName, qs.SupportsPersistence, qs.Clustered);

            // prevents the db manager from being garbage collected
            if (dbMgr is not null)
            {
                qs.AddNoGCObject(dbMgr);
            }

            return sched;
        }
        catch
        {
            await ShutdownFromInstantiateException(tp, qs, tpInited, qsInited).ConfigureAwait(false);
            throw;
        }
    }

    protected virtual string? GetNamedConnectionString(string dsConnectionStringName)
    {
        return null;
    }

    protected virtual T InstantiateType<T>(Type? implementationType)
    {
        return ObjectUtils.InstantiateType<T>(implementationType);
    }

    private async ValueTask ShutdownFromInstantiateException(IThreadPool? tp, QuartzScheduler? qs, bool tpInited, bool qsInited)
    {
        try
        {
            if (qsInited)
            {
                await qs!.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
            }
            else if (tpInited)
            {
                tp!.Shutdown(waitForJobsToComplete: false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Got another exception while shutting down after instantiation exception");
        }
    }

    protected virtual IScheduler Instantiate(QuartzSchedulerResources rsrcs, QuartzScheduler qs)
    {
        IScheduler sched = new StdScheduler(qs);
        return sched;
    }

    /// <summary>
    /// Needed while loadhelper is not constructed.
    /// </summary>
    /// <param name="typeName"></param>
    /// <returns></returns>
    protected virtual Type? LoadType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        return Type.GetType(typeName, throwOnError: true);
    }

    /// <summary>
    /// Returns a handle to the Scheduler produced by this factory.
    /// </summary>
    /// <remarks>
    /// If one of the <see cref="Initialize()" /> methods has not be previously
    /// called, then the default (no-arg) <see cref="Initialize()" /> method
    /// will be called by this method.
    /// </remarks>
    public virtual async ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        // We always need to guarantee exclusivity because of the possible sequence of interactions with
        // the SchedulerRepository.
        if (!await semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (cfg is null)
            {
                Initialize();
            }

            ISchedulerRepository schedulerRepository = GetSchedulerRepository();
            IScheduler? sched = schedulerRepository.Lookup(SchedulerName);

            if (sched is not null)
            {
                if (sched.IsShutdown)
                {
                    schedulerRepository.Remove(SchedulerName);
                }
                else
                {
                    return sched;
                }
            }

            sched = await Instantiate().ConfigureAwait(false);
            schedulerRepository.Bind(sched);
            return sched;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary> <para>
    /// Returns a handle to the Scheduler with the given name, if it exists (if
    /// it has already been instantiated).
    /// </para>
    /// </summary>
    public virtual ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        return new ValueTask<IScheduler?>(GetSchedulerRepository().Lookup(schedName));
    }
}