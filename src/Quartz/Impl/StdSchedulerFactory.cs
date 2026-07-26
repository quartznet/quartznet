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
using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// An implementation of <see cref="ISchedulerFactory" /> that creates a
/// <see cref="QuartzScheduler" /> instance from a flat collection of <c>quartz.*</c> properties.
/// </summary>
/// <remarks>
/// <para>
/// Properties are supplied by the caller, through <see cref="StdSchedulerFactory(NameValueCollection)"/>
/// or <see cref="Initialize(NameValueCollection)"/>, and are overridden by any <c>quartz.*</c>
/// environment variables. Nothing is read from disk: since 4.0 there is no <c>quartz.config</c> file
/// discovery. Prefer <c>AddQuartz</c> with <see cref="IConfiguration"/>, or
/// <see cref="QuartzSchedulerBuilder"/>, both of which configure a scheduler through the container.
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
public class StdSchedulerFactory : ISchedulerFactory, IDisposable
{
    private const string ConfigurationKeyPrefix = "quartz.";
    private const string ConfigurationKeyPrefixServer = "quartz.server";
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

    /// <summary>
    /// Guards building the private container, which two threads calling <c>GetScheduler</c> at once
    /// would otherwise both do — producing two schedulers, one of which loses the race to bind itself
    /// into the repository and is left running with nobody holding a reference to shut it down.
    /// </summary>
    private readonly Lock containerLock = new();

    private PropertiesParser cfg = null!;

    internal ILogger<StdSchedulerFactory> logger;

    private ServiceProvider? provider;
    private ISchedulerFactory? inner;
    private ISchedulerRepository? schedulerRepository;
    private bool disposed;

    private string SchedulerName
    {
        // ReSharper disable once ArrangeAccessorOwnerBody
        get { return cfg.GetStringProperty(PropertySchedulerInstanceName, defaultValue: "QuartzScheduler")!; }
    }

    /// <summary>
    /// Returns a handle to the default Scheduler, creating it if it does not
    /// yet exist.
    /// </summary>
    /// <remarks>
    /// Every call shares one factory, and therefore one scheduler. A factory owns its scheduler
    /// repository, so constructing a new one per call would find no existing scheduler to return and would
    /// build a second live scheduler carrying the same instance name and instance id as the first — two
    /// thread pools, two sets of connections, and against a clustered database two nodes checking in as
    /// the same instance.
    /// </remarks>
    /// <seealso cref="Initialize()">
    /// </seealso>
    public static ValueTask<IScheduler> GetDefaultScheduler(
        CancellationToken cancellationToken = default)
    {
        return defaultFactory.Value.GetScheduler(cancellationToken);
    }

    private static readonly Lazy<StdSchedulerFactory> defaultFactory = new(static () => new StdSchedulerFactory());

    /// <summary>
    /// Returns a handle to every scheduler this factory has produced.
    /// </summary>
    /// <remarks>
    /// This is no longer process-wide. Schedulers live in the repository belonging to the container that
    /// built them, so a scheduler created through <c>AddQuartz</c> or another
    /// <see cref="StdSchedulerFactory"/> is not listed here.
    /// </remarks>
    public virtual ValueTask<List<IScheduler>> GetAllSchedulers(
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<List<IScheduler>>(GetSchedulerRepository().LookupAll());
    }

    /// <summary>
    /// Returns the repository this factory's schedulers are bound into.
    /// </summary>
    /// <remarks>
    /// The repository is owned by this factory's container, so asking for it builds that container if it
    /// has not been built yet — there is no process-wide repository to fall back on.
    /// </remarks>
    protected virtual ISchedulerRepository GetSchedulerRepository()
    {
        Inner();
        return schedulerRepository!;
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
    /// Initializes the factory from the <c>quartz.*</c> environment variables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the entry point for applications that have not moved to <c>AddQuartz</c>. Nothing is read
    /// from disk: a scheduler is configured either by the properties handed to
    /// <see cref="Initialize(NameValueCollection)"/>, by environment variables, or — preferably — through
    /// the container with <see cref="IConfiguration"/> and <c>AddQuartz</c>, or with
    /// <see cref="QuartzSchedulerBuilder"/>.
    /// </para>
    /// <para>
    /// Configuring nothing at all is not a misconfiguration. Every setting has a typed default, so a
    /// scheduler with no configuration is a valid in-memory scheduler.
    /// </para>
    /// <para>
    /// This overload keeps the defaults that used to arrive from the embedded <c>quartz.config</c>, so a
    /// factory given no properties still produces the scheduler it always has. See
    /// <see cref="EmbeddedDefaults"/>.
    /// </para>
    /// </remarks>
    public virtual void Initialize()
    {
        // short-circuit if already initialized
        if (cfg is not null)
        {
            return;
        }

        logger = LogProvider.CreateLogger<StdSchedulerFactory>();
        WarnAboutIgnoredConfigurationFile();
        Initialize(OverrideWithSysProps(EmbeddedDefaults()));
    }

    /// <summary>
    /// Says so when a <c>quartz.config</c> that 3.x would have loaded is present but ignored.
    /// </summary>
    /// <remarks>
    /// Dropping file discovery without a word is the worst version of this change: an application that
    /// still ships a file selecting a database job store silently becomes an in-memory scheduler, its
    /// persisted triggers stop firing, and nothing in the log points at the file. This does not read the
    /// file — it only reports that something which used to be configuration no longer is.
    /// </remarks>
    private void WarnAboutIgnoredConfigurationFile()
    {
        var requestedFile = QuartzEnvironment.GetEnvironmentVariable(LegacyPropertiesFileVariable);
        var named = !string.IsNullOrWhiteSpace(requestedFile);
        var fileName = named ? requestedFile! : "~/quartz.config";
        var resolved = FileUtil.ResolveFile(fileName);

        if (!named && (resolved is null || !File.Exists(resolved)))
        {
            return;
        }

        logger.LogWarning(
            "Ignoring Quartz configuration file '{PropFileName}': configuration is no longer read from disk in 4.x. "
            + "Pass the properties to StdSchedulerFactory, or configure the scheduler through the container with "
            + "AddQuartz and IConfiguration. See the 4.x migration guide.",
            resolved ?? fileName);
    }

    /// <summary>
    /// The properties the <c>quartz.config</c> that used to ship as an embedded resource supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="Initialize()"/> uses these, because only that path ever read the file: handing the
    /// factory an explicit <see cref="NameValueCollection"/> always bypassed it, and so a scheduler
    /// configured that way fell back to the same internal defaults it still falls back to today. Putting
    /// these values on the typed options instead would therefore change behaviour for every caller rather
    /// than preserve it for this one.
    /// </para>
    /// <para>
    /// Environment variables are applied on top, and anything handed to
    /// <see cref="Initialize(NameValueCollection)"/> replaces the lot, so these only ever act as defaults.
    /// </para>
    /// </remarks>
    private static NameValueCollection EmbeddedDefaults()
    {
        return new NameValueCollection
        {
            [PropertySchedulerInstanceName] = "DefaultQuartzScheduler",
            ["quartz.threadPool.threadCount"] = "10",
            ["quartz.jobStore.misfireThreshold"] = "60000",
        };
    }

    /// <summary>
    /// The environment variable that used to name a <c>quartz.config</c> file.
    /// </summary>
    /// <remarks>
    /// No longer honoured — configuration does not come from a file any more. It is still recognised so
    /// that an application which still sets it is not failed by configuration validation over a variable
    /// that only looks like a setting.
    /// </remarks>
    private const string LegacyPropertiesFileVariable = "quartz.config";

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
            if (string.Equals(key, LegacyPropertiesFileVariable, StringComparison.Ordinal))
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

        lock (containerLock)
        {
            // Without this a call after Dispose would build a second container and hang it off the
            // disposed factory, where nothing will ever dispose it — and report an empty repository as
            // though the factory's schedulers had gone away.
            ObjectDisposedException.ThrowIf(disposed, this);

            return inner ??= BuildInner();
        }
    }

    private ISchedulerFactory BuildInner()
    {
        if (cfg is null)
        {
            Initialize();
        }

        var services = new ServiceCollection();

        // Plugins, execution limits and scheduler content are read from QuartzOptions, so the property
        // bag has to be there as well as bound onto the typed options.
        services.Configure<QuartzOptions>(options =>
        {
            foreach (var key in cfg!.UnderlyingProperties.AllKeys)
            {
                if (key is not null)
                {
                    options.Properties[key] = cfg.UnderlyingProperties[key];
                }
            }
        });

        QuartzPropertyBridge.Apply(services, cfg!.UnderlyingProperties);

        // Defaults last, so anything the properties selected explicitly is not beaten by its fallback.
        services.AddQuartzScheduler();

        provider = services.BuildServiceProvider();

        // Held on to so GetSchedulerRepository does not have to reach back into a provider that Dispose
        // may have replaced with null in the meantime.
        schedulerRepository = provider.GetRequiredService<ISchedulerRepository>();

        return provider.GetRequiredService<ISchedulerFactory>();
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
    public virtual ValueTask<IScheduler?> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Inner().GetScheduler(schedulerName, cancellationToken);
    }

    /// <summary>
    /// Loads a type by name, using the configured type load helper.
    /// </summary>
    protected virtual Type? LoadType(string? typeName)
    {
        return string.IsNullOrWhiteSpace(typeName) ? null : new SimpleTypeLoadHelper().LoadType(typeName);
    }

    /// <summary>
    /// Disposes the container this factory built, and with it the scheduler's container-owned parts.
    /// </summary>
    /// <remarks>
    /// Shut the scheduler down first. Disposing the factory releases what the container holds; it does
    /// not stop a running scheduler.
    /// </remarks>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the container this factory built.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/> rather than a finalizer.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        ServiceProvider? owned;
        lock (containerLock)
        {
            owned = provider;
            provider = null;
            inner = null;
            schedulerRepository = null;
            disposed = true;
        }

        owned?.Dispose();
    }
}
