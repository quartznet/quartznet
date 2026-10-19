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

using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// The flat <c>quartz.*</c> property keys, and what counts as one.
/// </summary>
/// <remarks>
/// <para>
/// These used to be public constants on <c>StdSchedulerFactory</c>, which was the only thing that read
/// them. They are internal now because a key is a string in a configuration file rather than a member
/// of an API: <see cref="QuartzPropertyBridge"/> translates them into typed options, and everything
/// downstream sees the options.
/// </para>
/// <para>
/// The strings themselves are unchanged, so every configuration file that worked before still works.
/// </para>
/// </remarks>
internal static class LegacyPropertyKeys
{
    internal const string Prefix = "quartz.";

    /// <summary>
    /// The prefix 3.x's <c>Quartz.Server</c> host used for its own settings, which are not scheduler
    /// settings.
    /// </summary>
    /// <remarks>
    /// The host itself is not part of 4.x, but a configuration file carried over from 3.x still has the
    /// keys in it, and treating one as a misspelled scheduler key would fail a startup over a setting
    /// that was never the scheduler's to begin with.
    /// </remarks>
    private const string ServerPrefix = "quartz.server";

    internal const string SchedulerInstanceName = "quartz.scheduler.instanceName";
    internal const string SchedulerInstanceId = "quartz.scheduler.instanceId";
    internal const string SchedulerInstanceIdGeneratorPrefix = "quartz.scheduler.instanceIdGenerator";
    internal const string SchedulerInstanceIdGeneratorType = SchedulerInstanceIdGeneratorPrefix + ".type";
    internal const string SchedulerThreadName = "quartz.scheduler.threadName";
    internal const string SchedulerBatchTimeWindow = "quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow";
    internal const string SchedulerMaxBatchSize = "quartz.scheduler.batchTriggerAcquisitionMaxCount";
    internal const string SchedulerIdleWaitTime = "quartz.scheduler.idleWaitTime";
    internal const string SchedulerMakeSchedulerThreadDaemon = "quartz.scheduler.makeSchedulerThreadDaemon";
    internal const string SchedulerTypeLoaderType = "quartz.scheduler.typeLoadHelper.type";
    internal const string SchedulerJobFactoryPrefix = "quartz.scheduler.jobFactory";
    internal const string SchedulerJobFactoryType = SchedulerJobFactoryPrefix + ".type";
    internal const string SchedulerInterruptJobsOnShutdown = "quartz.scheduler.interruptJobsOnShutdown";
    internal const string SchedulerInterruptJobsOnShutdownWithWait = "quartz.scheduler.interruptJobsOnShutdownWithWait";

    /// <summary>
    /// The execution groups a node serves, comma-separated; the flat spelling of
    /// <see cref="QuartzSchedulerOptions.ServedExecutionGroups" />.
    /// </summary>
    /// <remarks>
    /// A value key rather than a component key: it names groups, not a type, so it keeps binding when the
    /// component keys are switched off for a trimmed application. A group whose name holds a comma needs
    /// the typed option.
    /// </remarks>
    internal const string SchedulerServedExecutionGroups = "quartz.scheduler.servedExecutionGroups";

    /// <summary>
    /// The execution groups a node refuses, comma-separated; the flat spelling of
    /// <see cref="QuartzSchedulerOptions.RefusedExecutionGroups" />.
    /// </summary>
    internal const string SchedulerRefusedExecutionGroups = "quartz.scheduler.refusedExecutionGroups";

    /// <summary>
    /// <see cref="QuartzSchedulerOptions.DefaultMisfireHandling" />, by member name:
    /// <c>SmartPolicy</c>, <c>IgnoreMisfires</c>, <c>FireAndProceed</c> or <c>DoNothing</c>.
    /// </summary>
    internal const string SchedulerDefaultMisfireHandling = "quartz.scheduler.defaultMisfireHandling";
    internal const string SchedulerContextPrefix = "quartz.context.key";
    internal const string ThreadPoolPrefix = "quartz.threadPool";
    internal const string ThreadPoolType = "quartz.threadPool.type";
    internal const string TimeProviderType = "quartz.timeProvider.type";
    internal const string JobStorePrefix = "quartz.jobStore";

    /// <summary>
    /// The job store prefix with its separator, which is where a key under it starts.
    /// </summary>
    private const string JobStoreKeyPrefix = JobStorePrefix + ".";

    internal const string JobStoreType = "quartz.jobStore.type";
    internal const string JobStoreDbRetryInterval = "quartz.jobStore.dbRetryInterval";

    /// <summary>
    /// 3.22's <c>CommandTimeout</c>, in milliseconds, where zero means the provider's own default.
    /// </summary>
    /// <remarks>
    /// The key is under a supported prefix, so a configuration carrying it started a scheduler either
    /// way — with every statement, the lock statement included, left unbounded and nothing said about
    /// it. It is bridged rather than merely tolerated for that reason.
    /// </remarks>
    internal const string JobStoreCommandTimeout = "quartz.jobStore.commandTimeout";
    internal const string JobStoreLockHandlerPrefix = JobStorePrefix + ".lockHandler";
    internal const string JobStoreLockHandlerType = JobStoreLockHandlerPrefix + ".type";
    internal const string DataSourcePrefix = "quartz.dataSource";
    internal const string DbProvider = "quartz.dbprovider";
    internal const string ExecutionLimitPrefix = "quartz.executionLimit";

    /// <summary>
    /// The cluster-scoped twin of <see cref="ExecutionLimitPrefix" />: the same group keys and the same
    /// values, counted across every node sharing the job store rather than on this one.
    /// </summary>
    /// <remarks>
    /// A prefix of its own rather than a magic value under the existing one, because every key under
    /// <c>quartz.executionLimit</c> is a group name and every value is a count — there is no spelling
    /// left in either half that could not also be a real group or a real number.
    /// </remarks>
    internal const string ClusterExecutionLimitPrefix = "quartz.clusterExecutionLimit";
    internal const string PluginPrefix = "quartz.plugin";
    internal const string PluginType = "type";
    internal const string CheckConfiguration = "quartz.checkConfiguration";
    internal const string ThreadExecutor = "quartz.threadExecutor";
    internal const string ObjectSerializer = "quartz.serializer";

    /// <summary>
    /// The instance id values that select a generator rather than naming one.
    /// </summary>
    internal const string AutoGenerateInstanceId = "AUTO";

    /// <inheritdoc cref="AutoGenerateInstanceId" />
    internal const string SystemPropertyAsInstanceId = "SYS_PROP";

    /// <summary>
    /// What to do instead of naming the lock handler's scheduler in configuration.
    /// </summary>
    /// <remarks>
    /// Shared by both spellings of the key. 3.x's <c>ITablePrefixAware</c> declared the property as
    /// <c>SchedName</c> and <c>StdSchedulerFactory</c> wrote it through the lock handler's property
    /// group, so a configuration file carried over from 3.x spells it <c>schedName</c>; the property
    /// is <c>SchedulerName</c> here, which is what someone re-deriving the key from the type writes.
    /// Both name the same dead seam, and both are worth the same explanation.
    /// </remarks>
    private const string LockHandlerSchedulerNameAdvice =
        "The job store tells the lock handler which scheduler it locks for through "
        + "ILockHandler.Initialize, using the scheduler's own instance name. Remove this key.";

    /// <summary>
    /// The key prefixes a scheduler understands, used to reject a misspelled one.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so that the completeness test can read it: rejecting an unknown key
    /// is only safe while this list covers every key some reader still consults, and that is a property
    /// of the pair rather than of either list on its own.
    /// </remarks>
    internal static readonly string[] supportedKeys =
    [
        SchedulerInstanceName,
        SchedulerInstanceId,
        SchedulerInstanceIdGeneratorPrefix,
        SchedulerBatchTimeWindow,
        SchedulerMaxBatchSize,
        SchedulerIdleWaitTime,
        SchedulerTypeLoaderType,
        SchedulerJobFactoryPrefix,
        SchedulerInterruptJobsOnShutdown,
        SchedulerInterruptJobsOnShutdownWithWait,
        SchedulerServedExecutionGroups,
        SchedulerRefusedExecutionGroups,
        SchedulerDefaultMisfireHandling,
        SchedulerContextPrefix,
        ThreadPoolPrefix,
        TimeProviderType,
        JobStorePrefix,
        DataSourcePrefix,
        DbProvider,
        ExecutionLimitPrefix,
        ClusterExecutionLimitPrefix,
        PluginPrefix,
        CheckConfiguration,
        ThreadExecutor,
        ObjectSerializer,
    ];

    /// <summary>
    /// How the rest of a key under one of <see cref="namedChildPrefixes" /> is made up.
    /// </summary>
    internal enum NameShape
    {
        /// <summary>
        /// All of it is one name: an execution group key such as <c>tenant:*</c>, or a context key.
        /// </summary>
        WholeKey,

        /// <summary>
        /// The first segment is a name, and what follows it is that thing's properties.
        /// </summary>
        NameThenProperties,
    }

    /// <summary>
    /// The prefixes whose children are names the user chose rather than property names, spelled as
    /// their readers spell them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property name is matched without regard to case, so a hierarchical section can camel-case it on
    /// the way to the flat key. A name cannot be: each of these is compared ordinally by something
    /// downstream — an execution group by the limits, a context key by <see cref="SchedulerContext" />,
    /// a data source by named options, and a driver description by the metadata resolver.
    /// <c>QuartzConfigurationHelper</c> reads this table to keep each name as the configuration wrote it.
    /// </para>
    /// <para>
    /// A plugin's name is left camel-cased on purpose. Nothing compares it ordinally — a plugin added in
    /// code is matched without regard to case — and the XML plugin names its persisted file-scan job and
    /// trigger after it, so a change of case would leave the previous version's job behind, failing on
    /// every scan.
    /// </para>
    /// <para>
    /// The readers match the prefixes themselves ordinally too, which configuration keys, being
    /// case-insensitive, do not promise: <c>Quartz:DbProvider</c> camel-cases to
    /// <c>quartz.dbProvider</c>, which is not <see cref="DbProvider" />. So a section that matches one of
    /// these without regard to case is written in the spelling here.
    /// </para>
    /// <para>
    /// The listener prefixes in <see cref="removedKeys" /> also held names, but nothing reads them any
    /// more, and Quartz has no flat key for a calendar.
    /// </para>
    /// </remarks>
    internal static readonly (string Prefix, NameShape Shape)[] namedChildPrefixes =
    [
        (ExecutionLimitPrefix, NameShape.WholeKey),
        (ClusterExecutionLimitPrefix, NameShape.WholeKey),
        (SchedulerContextPrefix, NameShape.WholeKey),
        (DataSourcePrefix, NameShape.NameThenProperties),
        (DbProvider, NameShape.NameThenProperties),
    ];

    /// <summary>
    /// Keys Quartz used to read, and what to do instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported by name rather than as merely unknown: a configuration that still carries one of these
    /// was configuring something real, and "unknown property" reads like a typo.
    /// </para>
    /// <para>
    /// Internal rather than private for the same reason as <see cref="supportedKeys" />: no key listed
    /// here may still be one a reader consults, and only a test that sees both lists can say so.
    /// </para>
    /// </remarks>
    internal static readonly (string Prefix, string Advice)[] removedKeys =
    [
        ("quartz.scheduler.proxy",
            "Remoting a scheduler is not supported on modern .NET. Talk to a remote scheduler over HTTP "
            + "with the Quartz.HttpClient package (AddQuartzHttpClient), which serves the same purpose."),
        ("quartz.scheduler.exporter",
            "Remoting a scheduler is not supported on modern .NET. Expose a scheduler over HTTP with the "
            + "Quartz.AspNetCore package (AddQuartzHttpApi and MapQuartzHttpApi) instead."),
        (JobStoreLockHandlerPrefix + ".tablePrefix",
            "The job store tells the lock handler its table prefix through ILockHandler.Initialize, "
            + "using the value of 'quartz.jobStore.tablePrefix'. Set that key instead and remove this one."),
        (JobStoreLockHandlerPrefix + ".schedName", LockHandlerSchedulerNameAdvice),
        (JobStoreLockHandlerPrefix + ".schedulerName", LockHandlerSchedulerNameAdvice),
        (SchedulerThreadName,
            "The scheduling loop is a Task rather than a Thread, so there is no thread of its own to "
            + "name and this key had no effect in 4.0. Remove it."),
        (SchedulerMakeSchedulerThreadDaemon,
            "The scheduling loop is a Task rather than a Thread, so it never kept a process alive and "
            + "this key had no effect in 4.0. Remove it. For the job store's misfire and cluster "
            + "threads, which are real threads, set 'quartz.jobStore.makeThreadsDaemons' or "
            + "AdoJobStoreOptions.UseBackgroundThreads."),
        ("quartz.jobListener",
            "A listener named by configuration could carry no matchers, so it heard every job. Register "
            + "it with AddJobListener<T>(matchers) instead, which takes the matchers that say which jobs "
            + "it hears and constructs the listener through the container."),
        ("quartz.triggerListener",
            "A listener named by configuration could carry no matchers, so it heard every trigger. "
            + "Register it with AddTriggerListener<T>(matchers) instead, which takes the matchers that "
            + "say which triggers it hears and constructs the listener through the container."),
    ];

    /// <summary>
    /// What the reflective half of the property bridge says for itself. Every call into it is guarded by
    /// <see cref="FeatureSwitches.IsLegacyComponentKeysSupported" />, so an application that reads the
    /// warning is one that turned the switch back on.
    /// </summary>
    internal const string ComponentKeysNameTypes =
        "A quartz.* key that names a component by type, or sets a component's properties by string, is read by "
        + "reflection, which a trimmed application cannot rely on; " + FeatureSwitches.LegacyComponentKeysSwitch
        + " guards every call.";

    /// <summary>
    /// What to write in code instead of a key the switch refuses, by the prefix the key is under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Advice only: which keys are refused is each reader's decision, made where the key is read, so
    /// that Quartz's own spellings of <c>quartz.jobStore.type</c>, <c>quartz.threadPool.type</c> and
    /// <c>quartz.serializer.type</c>, and <c>SYS_PROP</c>, can keep working through a <c>typeof</c>.
    /// The <c>quartz.dataSource</c> entry is here for the one key under it that names a type,
    /// <c>quartz.dataSource.&lt;name&gt;.connectionProvider.type</c>; the rest are values.
    /// </para>
    /// <para>
    /// Internal rather than private so that the completeness test can hold each entry to a prefix the
    /// validator accepts and to advice that names a member of the builder.
    /// </para>
    /// </remarks>
    internal static readonly (string Prefix, string Advice)[] componentKeyAdvice =
    [
        (SchedulerTypeLoaderType, "In code, UseTypeLoader<T>()."),
        (SchedulerInstanceIdGeneratorPrefix,
            "In code, UseInstanceIdGenerator<T>(); 'quartz.scheduler.instanceId = SYS_PROP' on its own still "
            + "selects the system property generator."),
        (SchedulerJobFactoryType, "In code, UseJobFactory<T>()."),
        (TimeProviderType, "In code, UseTimeProvider(timeProvider)."),
        (ThreadPoolType, "In code, UseThreadPool<T>(); Quartz's own DefaultThreadPool and SimpleThreadPool spellings are still read."),
        (JobStorePrefix + ".driverDelegateType",
            "In code, UsePersistentStore(s => s.UseSqlServer(factory, connectionString)) or its siblings, which "
            + "choose the delegate, or UsePersistentStore(s => s.UseDriverDelegate<T>())."),
        (JobStorePrefix + ".driverDelegateInitString", "In code, UsePersistentStore(s => s.UseTriggerPersistenceDelegate<T>())."),
        (JobStoreLockHandlerPrefix, "In code, UsePersistentStore(s => s.UseLockHandler<T>())."),
        (JobStoreType,
            "In code, UseInMemoryStore(), UsePersistentStore(s => s.UseSqlServer(factory, connectionString)) or "
            + "its siblings, or UseJobStore<T>(). Quartz's own RAMJobStore spelling is still read, and a key naming "
            + "JobStoreTX or JobStoreCMT beside a UsePersistentStore call is left to the code, as it always was; a "
            + "persistent store named by keys alone cannot run in this publish, since its driver is named by type."),
        (DataSourcePrefix, "In code, UsePersistentStore(s => s.UseConnectionProvider<T>())."),
        (ObjectSerializer,
            "In code, UseSystemTextJsonSerializer() or UseNewtonsoftJsonSerializer(); 'stj' and 'json' on their own "
            + "are still read."),
        (DbProvider, "In code, UsePersistentStore(s => s.UseGenericDatabase(provider, metadata: () => new DbMetadata { ... }))."),
        (PluginPrefix, "In code, AddPlugin<T>() or the plugin's own Use* method."),
    ];

    /// <summary>
    /// What a key the switch refuses is told: the key, why, the code form, and the property that turns
    /// the key back on.
    /// </summary>
    /// <param name="key">The key as the configuration spelled it.</param>
    internal static string ComponentKeyRefused(string key)
    {
        string advice = "";
        foreach ((string prefix, string candidate) in componentKeyAdvice)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                advice = candidate + " ";
                break;
            }
        }

        return $"Configuration property '{key}' names a component or sets its properties by string, which this "
            + $"publish does not do ({FeatureSwitches.LegacyComponentKeysSwitch} is false). {advice}Set "
            + $"<{FeatureSwitches.LegacyComponentKeysProperty}>true</{FeatureSwitches.LegacyComponentKeysProperty}> "
            + "to read such keys again.";
    }

    /// <summary>
    /// Refuses the first key under a prefix that the switch is about, leaving out the ones it names.
    /// </summary>
    /// <remarks>
    /// For a component Quartz builds from a <c>typeof</c> when the switch is off: the component is fine,
    /// and any further key under its prefix would have been written onto it by name, which is not.
    /// </remarks>
    /// <param name="properties">The bag to look in.</param>
    /// <param name="prefix">The component's prefix, without the trailing separator.</param>
    /// <param name="allowed">The keys directly under the prefix that are values rather than properties.</param>
    internal static void RefuseComponentKeysUnder(NameValueCollection properties, string prefix, params string[] allowed)
    {
        string start = prefix + ".";
        foreach (string? key in properties.AllKeys)
        {
            if (key is null || !key.StartsWith(start, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = key[start.Length..];
            if (!allowed.Contains(rest, StringComparer.OrdinalIgnoreCase))
            {
                Throw.SchedulerConfigException(ComponentKeyRefused(key));
            }
        }
    }

    /// <summary>
    /// Rejects a <c>quartz.*</c> key no reader understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A misspelled key is otherwise read by nobody and reported by nothing: <c>quartz.jobstore.type</c>
    /// differs from <c>quartz.jobStore.type</c> by one letter and turns a database-backed scheduler into
    /// an in-memory one without a word. Set <c>quartz.checkConfiguration</c> to <see langword="false"/>
    /// to allow keys of your own — a third-party component configured through this bag, for instance.
    /// </para>
    /// <para>
    /// Applied wherever a caller hands Quartz a flat property bag it wrote itself:
    /// <c>QuartzSchedulerBuilder.UseProperties</c> and the <c>AddQuartz(services, properties, …)</c>
    /// overloads. That last group is the commonest shape a 3.x application migrates in, so it is exactly
    /// the caller the removed-key advice is written for. Keys flattened out of an
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> section are not checked, because
    /// there every section becomes a <c>quartz.*</c> key whether Quartz reads it or not.
    /// </para>
    /// </remarks>
    internal static void Validate(NameValueCollection properties)
    {
        var parser = new PropertiesParser(properties);
        if (!parser.GetBooleanProperty(CheckConfiguration, defaultValue: true))
        {
            return;
        }

        foreach (var key in properties.AllKeys)
        {
            if (key is null
                || !key.StartsWith(Prefix, StringComparison.Ordinal)
                || key.StartsWith(ServerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (prefix, advice) in removedKeys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    Throw.SchedulerConfigException($"Configuration property '{key}' is no longer read. {advice}");
                }
            }

            if (!IsSupported(key))
            {
                Throw.SchedulerConfigException(
                    $"Unknown configuration property '{key}'. Set '{CheckConfiguration}' to false to allow keys Quartz does not read.");
            }
        }
    }

    private static bool IsSupported(string key)
    {
        foreach (var supportedKey in supportedKeys)
        {
            if (key.StartsWith(supportedKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every <c>quartz.jobStore.*</c> key some reader of the flat format consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One table rather than the reading order, because <see cref="ValidateJobStoreKeys" /> has to know
    /// what was consumed and the readers are spread over four methods of
    /// <see cref="QuartzPropertyBridge" /> — the ADO.NET store's options, the in-memory store's misfire
    /// threshold, the clustering options and the registrations. A key added to one of them and not to
    /// this table is refused although it works, which is why
    /// <c>EveryJobStoreKeyTheReadersConsultIsAcceptedForAnAdoStore</c> compares the two mechanically.
    /// </para>
    /// <para>
    /// Both spellings of a setting are keys: <c>clustered</c> and <c>clustering.enabled</c> say the same
    /// thing, as do <c>performSchemaValidation</c> and <c>schemaProvisioning</c>.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> jobStoreKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        JobStoreType,
        JobStoreDbRetryInterval,
        JobStoreCommandTimeout,
        JobStorePrefix + ".dataSource",
        JobStorePrefix + ".driverDelegateType",
        JobStorePrefix + ".driverDelegateInitString",
        JobStorePrefix + ".tablePrefix",
        JobStorePrefix + ".useProperties",
        JobStorePrefix + ".misfireThreshold",
        JobStorePrefix + ".misfireHandlerFrequency",
        JobStorePrefix + ".maxMisfiresToHandleAtATime",
        JobStorePrefix + ".maxTransientRetries",
        JobStorePrefix + ".transientRetryInterval",
        JobStorePrefix + ".retryableActionErrorLogThreshold",
        JobStorePrefix + ".maxConsecutiveFireFailures",
        JobStorePrefix + ".recoverFiringsCancelledByShutdown",
        JobStorePrefix + ".makeThreadsDaemons",
        JobStorePrefix + ".useDBLocks",
        JobStorePrefix + ".lockOnInsert",
        JobStorePrefix + ".acquireTriggersWithinLock",
        JobStorePrefix + ".lockFreeFiringRounds",
        JobStorePrefix + ".acceptEnlistedTransactions",
        JobStorePrefix + ".openConnection",
        JobStorePrefix + ".txIsolationLevelSerializable",
        JobStorePrefix + ".doubleCheckLockMisfireHandler",
        JobStorePrefix + ".performSchemaValidation",
        JobStorePrefix + ".schemaProvisioning",
        JobStorePrefix + ".executionHistory",
        JobStorePrefix + ".selectWithLockSQL",
        JobStorePrefix + ".clustered",
        JobStorePrefix + ".clusterCheckinInterval",
        JobStorePrefix + ".clusterCheckinMisfireThreshold",
        JobStorePrefix + ".clustering.enabled",
        JobStorePrefix + ".clustering.checkinInterval",
        JobStorePrefix + ".clustering.checkinMisfireThreshold",
    };

    /// <summary>
    /// The same keys under their typed spelling, read off the options types rather than listed.
    /// </summary>
    /// <remarks>
    /// A hierarchical <c>JobStore</c> section binds onto <see cref="AdoJobStoreOptions" /> and is
    /// flattened onto this prefix as well, so <c>JobStore:StoreJobDataAsStrings</c> arrives as
    /// <c>quartz.jobStore.storeJobDataAsStrings</c> — a key the bridge never reads and the typed binder
    /// always does. Taken from the options types so that a property added to either needs no entry here:
    /// a new setting would otherwise be refused in the very spelling the options type gives it.
    /// </remarks>
    private static readonly HashSet<string> typedJobStoreKeys = TypedJobStoreKeys();

    private static HashSet<string> TypedJobStoreKeys()
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

        foreach (PropertyInfo property in typeof(AdoJobStoreOptions).GetProperties())
        {
            keys.Add(JobStoreKeyPrefix + property.Name);
        }

        // Clustering is a sub-section of the job store's, which flattens onto this prefix.
        foreach (PropertyInfo property in typeof(ClusteringOptions).GetProperties())
        {
            keys.Add(JobStoreKeyPrefix + "clustering." + property.Name);
        }

        return keys;
    }

    /// <summary>
    /// Rejects a <c>quartz.jobStore.*</c> key the ADO.NET job store does not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Validate" /> accepts any key under a supported <em>prefix</em>, and
    /// <c>quartz.jobStore</c> is one, so a misspelling underneath it was accepted by the check and then
    /// consulted by nobody: <c>quartz.jobStore.dbRetryIntreval</c> started the scheduler with the
    /// default retry interval in force and said nothing about it. 3.x wrote every key under this prefix
    /// onto the store object by name and failed startup on one the store had no property for, and a
    /// store whose settings arrive as typed options has to give the same answer — a key a newer 3.x line
    /// adds and 4.x has not bridged is dropped the same way a typo is, which is how
    /// <see cref="JobStoreCommandTimeout" /> went missing.
    /// </para>
    /// <para>
    /// Called from the ADO.NET store's own options mapping, so it runs when those options are resolved —
    /// the startup validation a persistent store declares, or the scheduler build that constructs the
    /// store — and not at all for a store that reads none of them. A third-party store's keys are
    /// written onto the store object by name, which reports an unknown one itself, so the two kinds of
    /// store agree in outcome without this being asked about either.
    /// </para>
    /// <para>
    /// Two families under the prefix take their own route and are not refused here: every
    /// <c>lockHandler.*</c> key is written onto the lock handler by name, and
    /// <c>driverDelegateInitString</c> is parsed into trigger persistence delegate registrations. Both
    /// report an unknown setting of their own.
    /// </para>
    /// </remarks>
    internal static void ValidateJobStoreKeys(NameValueCollection properties)
    {
        var parser = new PropertiesParser(properties);
        if (!parser.GetBooleanProperty(CheckConfiguration, defaultValue: true))
        {
            return;
        }

        foreach (var key in properties.AllKeys)
        {
            // Case-insensitively, because that is how the bag itself looks a key up: a reader asking for
            // 'quartz.jobStore.tablePrefix' is answered by a key written 'quartz.jobstore.tableprefix',
            // so refusing by exact spelling would refuse a key that configures the store.
            if (key is null
                || key.Length <= JobStoreKeyPrefix.Length
                || !key.StartsWith(JobStoreKeyPrefix, StringComparison.OrdinalIgnoreCase)
                || IsJobStoreKey(key))
            {
                continue;
            }

            Throw.SchedulerConfigException(
                $"Unknown configuration property '{key}'. It is not a setting of the ADO.NET job store, "
                + $"and no other reader consults it. Set '{CheckConfiguration}' to false to allow keys Quartz does not read.");
        }
    }

    private static bool IsJobStoreKey(string key)
    {
        return IsUnderLockHandler(key) || jobStoreKeys.Contains(key) || typedJobStoreKeys.Contains(key);
    }

    /// <summary>
    /// Whether the key is the lock handler's prefix or a key beneath it.
    /// </summary>
    /// <remarks>
    /// The separator is required rather than matching the prefix alone, so that
    /// <c>quartz.jobStore.lockHandlerType</c> — the dot left out — is the unknown key it is rather than
    /// something a lock handler will be asked about later.
    /// </remarks>
    private static bool IsUnderLockHandler(string key)
    {
        return key.StartsWith(JobStoreLockHandlerPrefix, StringComparison.OrdinalIgnoreCase)
               && (key.Length == JobStoreLockHandlerPrefix.Length || key[JobStoreLockHandlerPrefix.Length] == '.');
    }
}
