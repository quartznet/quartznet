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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
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
    public const string PropertyExecutionLimitPrefix = "quartz.executionLimit";
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
        PropertyExecutionLimitPrefix,
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


    private PropertiesParser cfg = null!;

    internal ILogger<StdSchedulerFactory> logger;

    private ServiceProvider? provider;
    private ISchedulerFactory? inner;

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
    /// Initializes the factory with defaults, overridden by any <c>quartz.*</c> environment variables.
    /// </summary>
    /// <remarks>
    /// Scheduler configuration is no longer discovered from a <c>quartz.config</c> file. Supply it
    /// through <see cref="IConfiguration"/> and <c>AddQuartz</c>, or in code through
    /// <see cref="QuartzSchedulerBuilder"/>.
    /// </remarks>
    public virtual void Initialize()
    {
        // short-circuit if already initialized
        if (cfg is not null)
        {
            return;
        }

        logger = LogProvider.CreateLogger<StdSchedulerFactory>();
        Initialize(OverrideWithSysProps(new NameValueCollection()));
    }

    /// <summary>
    /// Reads the <c>quartz.config</c> file, if present.
    /// </summary>
    /// <remarks>
    /// Scheduler configuration no longer comes from this file. It survives only so that custom ADO.NET
    /// provider metadata, declared with <c>quartz.dbprovider.*</c> keys, can still be defined in one —
    /// there is no typed equivalent for that yet.
    /// </remarks>
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
            Throw.SchedulerConfigException(
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
                Throw.SchedulerConfigException($"Unknown configuration property '{configurationKey}'");
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

    /// <summary>
    /// Builds the container this factory resolves its scheduler from, if it has not been built yet.
    /// </summary>
    /// <remarks>
    /// The scheduler is constructed from a container even here, so the properties-based entry point and
    /// <c>AddQuartz</c> share one construction path rather than having a reflective one of their own.
    /// The container is owned by this factory and lives as long as it does.
    /// </remarks>
    private ISchedulerFactory Inner()
    {
        if (inner is not null)
        {
            return inner;
        }

        if (cfg is null)
        {
            Initialize();
        }


        var services = new ServiceCollection();

        // Callers of this entry point look schedulers up through the process-wide repository and
        // connection manager, so the container must share those rather than owning private ones.
        services.AddSingleton<ISchedulerRepository>(SchedulerRepository.Instance);
        services.AddSingleton<IDbConnectionManager>(DBConnectionManager.Instance);

        // Plugins, execution limits and scheduler content are read from QuartzOptions, so the property
        // bag has to be there as well as bound onto the typed options.
        services.Configure<QuartzOptions>(options =>
        {
            foreach (var key in cfg!.UnderlyingProperties.AllKeys)
            {
                if (key is not null)
                {
                    options[key] = cfg.UnderlyingProperties[key];
                }
            }
        });

        QuartzPropertyBridge.Apply(services, cfg!.UnderlyingProperties);

        // Defaults last, so anything the properties selected explicitly is not beaten by its fallback.
        services.AddQuartzScheduler();

        provider = services.BuildServiceProvider();
        inner = provider.GetRequiredService<ISchedulerFactory>();
        return inner;
    }

    /// <summary>
    /// Returns a handle to the scheduler produced by this factory, creating it if it does not yet exist.
    /// </summary>
    public virtual ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        return Inner().GetScheduler(cancellationToken);
    }

    /// <summary>
    /// Returns a handle to the scheduler with the given name, if it exists.
    /// </summary>
    public virtual ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        return Inner().GetScheduler(schedName, cancellationToken);
    }

    /// <summary>
    /// Loads a type by name, using the configured type load helper.
    /// </summary>
    protected virtual Type? LoadType(string? typeName)
    {
        return string.IsNullOrWhiteSpace(typeName) ? null : new SimpleTypeLoadHelper().LoadType(typeName);
    }
}
