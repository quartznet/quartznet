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

using Microsoft.Extensions.Logging;

namespace Quartz.Configuration;

/// <summary>
/// Every event configuration, dependency injection and hosting log — the parts a container builds a
/// scheduler out of — as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 4000-4999 belong to this area and are allocated in file order: the configuration
/// diagnostics themselves first, then the scheduler factory, the instance id generator, the job
/// factories and the thread pools. Those last live in <c>Quartz.Impl</c> and reach these methods
/// through a <c>using Quartz.Configuration;</c>: one class per area is what makes an id range mean
/// something, and the area spans two namespaces.
/// </para>
/// <para>
/// An id, once given out, is what an operator filters and alerts on, so it is never reused for a
/// different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </para>
/// </remarks>
internal static partial class ConfigurationLog
{
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "The shared-database check could not read what database scheduler '{SchedulerName}' talks to.")]
    public static partial void SharedDatabaseCheckUnavailable(this ILogger logger, string schedulerName, Exception exception);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Scheduler '{SchedulerName}' (data source '{DataSource}', table prefix '{TablePrefix}') and scheduler '{OtherSchedulerName}' (data source '{OtherDataSource}', table prefix '{OtherTablePrefix}') use the same database with different table prefixes, so neither can see the other's rows. Schedulers sharing a database are normally told apart by SCHED_NAME and share one table prefix; separate table sets are legal, and if that is what you meant this warning is expected. If it is not, the scheduler with the wrong prefix starts cleanly, passes schema validation against the tables it was pointed at, and never sees its own data.")]
    public static partial void SchedulersShareDatabaseWithDifferentPrefixes(
        this ILogger logger,
        string schedulerName,
        string dataSource,
        string tablePrefix,
        string otherSchedulerName,
        string otherDataSource,
        string otherTablePrefix);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Quartz Scheduler {Version} - '{SchedulerName}' with instanceId '{SchedulerInstanceId}' initialized")]
    public static partial void SchedulerInitialized(this ILogger logger, string version, string schedulerName, string schedulerInstanceId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "Using thread pool '{ThreadPoolType}', size: {ThreadPoolSize}")]
    public static partial void UsingThreadPool(this ILogger logger, string? threadPoolType, int threadPoolSize);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information, Message = "Using job store '{JobStoreType}', supports persistence: {SupportsPersistence}, clustered: {Clustered}")]
    public static partial void UsingJobStore(this ILogger logger, string? jobStoreType, bool supportsPersistence, bool clustered);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Error, Message = "Couldn't generate instance id")]
    public static partial void InstanceIdGenerationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Error, Message = "Got another exception while shutting down after instantiation exception")]
    public static partial void ShutdownAfterInstantiationFailureFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Information, Message = "Host name '{HostName}' was too long, shortened to '{Newname}'")]
    public static partial void HostNameShortened(this ILogger logger, string hostName, string newname);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Warning, Message = "Failed to return a job after its creation failed; the original error follows")]
    public static partial void JobReturnAfterFailedCreationFailed(this ILogger logger, Exception exception);

    /// <remarks>
    /// The whole message is one placeholder because it is assembled at the call site out of the job
    /// type, the property and the value that would not go into it, and there are nine call sites that
    /// each word it differently. The text an operator reads is unchanged, the event id is what they
    /// filter on, and the <c>CA2254</c> pragma that used to stand here is gone.
    /// </remarks>
    [LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "{Problem}")]
    public static partial void JobPropertyNotSet(this ILogger logger, string problem, Exception? exception);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Debug, Message = "Producing instance of Job '{JobKey}', class={JobFullName}")]
    public static partial void ProducingJobInstance(this ILogger logger, JobKey jobKey, string? jobFullName);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Debug, Message = "TaskSchedulingThreadPool configured with max concurrency of {MaxConcurrency} and TaskScheduler {SchedulerName}.")]
    public static partial void TaskSchedulingThreadPoolConfigured(this ILogger logger, int maxConcurrency, string schedulerName);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Error, Message = "A task handed to the thread pool faulted.")]
    public static partial void ThreadPoolTaskFaulted(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Debug, Message = "Shutting down threadpool...")]
    public static partial void ThreadPoolShuttingDown(this ILogger logger);

    [LoggerMessage(EventId = 4014, Level = LogLevel.Debug, Message = "No executing jobs remaining, all threads stopped.")]
    public static partial void ThreadPoolDrained(this ILogger logger);

    [LoggerMessage(EventId = 4015, Level = LogLevel.Debug, Message = "Draining threadpool...")]
    public static partial void ThreadPoolDraining(this ILogger logger);

    [LoggerMessage(EventId = 4016, Level = LogLevel.Debug, Message = "Gave up waiting for the thread pool to drain; work is still running.")]
    public static partial void ThreadPoolDrainGivenUp(this ILogger logger);

    [LoggerMessage(EventId = 4017, Level = LogLevel.Debug, Message = "Thread pool closed to new work with {RunningTaskCount} running tasks remaining.")]
    public static partial void ThreadPoolClosedToNewWork(this ILogger logger, int runningTaskCount);

    [LoggerMessage(EventId = 4018, Level = LogLevel.Debug, Message = "Shutdown of threadpool complete.")]
    public static partial void ThreadPoolShutdownComplete(this ILogger logger);

    [LoggerMessage(EventId = 4019, Level = LogLevel.Debug, Message = "Shutdown complete")]
    public static partial void ZeroSizeThreadPoolShutdownComplete(this ILogger logger);
}
