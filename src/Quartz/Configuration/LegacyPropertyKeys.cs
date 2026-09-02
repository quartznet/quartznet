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
    internal const string SchedulerContextPrefix = "quartz.context.key";
    internal const string ThreadPoolPrefix = "quartz.threadPool";
    internal const string ThreadPoolType = "quartz.threadPool.type";
    internal const string TimeProviderType = "quartz.timeProvider.type";
    internal const string JobStorePrefix = "quartz.jobStore";
    internal const string JobStoreType = "quartz.jobStore.type";
    internal const string JobStoreDbRetryInterval = "quartz.jobStore.dbRetryInterval";
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
}
