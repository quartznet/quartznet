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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Every event clustering logs — check-in, failed-instance detection and recovery — as source-generated
/// methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 3500-3599 belong to this area and are allocated in file order: the cluster region of
/// <see cref="AdoJobStoreBase" /> first, then <see cref="ClusterManager" />.
/// </para>
/// <para>
/// The six counting events (3503, 3507-3511) were <c>LogWarnIfNonZero</c>, which logged at Information
/// when the count was non-zero and Debug when it was not, whatever its name said. They are Warning
/// events now, raised only when the count is non-zero — the level the name always claimed, and the
/// only one of the two branches that ever carried news.
/// </para>
/// <para>
/// An id, once given out, is what an operator filters and alerts on, so it is never reused for a
/// different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </para>
/// </remarks>
internal static partial class ClusterLog
{
    [LoggerMessage(EventId = 3500, Level = LogLevel.Warning, Message = "Transient exception on attempt {Attempt} of {TotalAttempts} in DoCheckin, will retry after {RetryInterval}")]
    public static partial void TransientFailureInCheckIn(this ILogger logger, int attempt, int totalAttempts, TimeSpan retryInterval, Exception exception);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Warning, Message = "This scheduler instance ({InstanceId}) is still active but was recovered by another instance in the cluster.  This may cause inconsistent behavior.")]
    public static partial void RecoveredByAnotherInstance(this ILogger logger, string instanceId);

    [LoggerMessage(EventId = 3502, Level = LogLevel.Warning, Message = "Found orphaned fired triggers for instance: {SchedulerInstanceId}")]
    public static partial void OrphanedFiredTriggersFound(this ILogger logger, string schedulerInstanceId);

    [LoggerMessage(EventId = 3503, Level = LogLevel.Warning, Message = "ClusterManager: detected {Count} failed or restarted instances.")]
    public static partial void FailedInstancesDetected(this ILogger logger, int count);

    [LoggerMessage(EventId = 3504, Level = LogLevel.Information, Message = "ClusterManager: Scanning for instance {SchedulerInstanceId}'s failed in-progress jobs.")]
    public static partial void ScanningFailedInstance(this ILogger logger, string schedulerInstanceId);

    [LoggerMessage(EventId = 3505, Level = LogLevel.Information, Message = "ClusterManager: Deferring recovery of [DisallowConcurrentExecution] job {JobKey} (fired trigger {FireInstanceId}) — may still be executing on instance {SchedulerInstanceId}.")]
    public static partial void RecoveryDeferred(this ILogger logger, JobKey? jobKey, string fireInstanceId, string schedulerInstanceId);

    [LoggerMessage(EventId = 3506, Level = LogLevel.Warning, Message = "ClusterManager: failed job {JobKey} no longer exists, cannot schedule recovery.")]
    public static partial void FailedJobNoLongerExists(this ILogger logger, JobKey? jobKey);

    [LoggerMessage(EventId = 3507, Level = LogLevel.Warning, Message = "ClusterManager: ......Freed {Count} acquired trigger(s).")]
    public static partial void AcquiredTriggersFreed(this ILogger logger, int count);

    [LoggerMessage(EventId = 3508, Level = LogLevel.Warning, Message = "ClusterManager: ......Deleted {Count} complete trigger(s).")]
    public static partial void CompleteTriggersDeleted(this ILogger logger, int count);

    [LoggerMessage(EventId = 3509, Level = LogLevel.Warning, Message = "ClusterManager: ......Scheduled {Count} recoverable job(s) for recovery.")]
    public static partial void RecoverableJobsScheduled(this ILogger logger, int count);

    [LoggerMessage(EventId = 3510, Level = LogLevel.Warning, Message = "ClusterManager: ......Cleaned-up {Count} other failed job(s).")]
    public static partial void OtherFailedJobsCleanedUp(this ILogger logger, int count);

    [LoggerMessage(EventId = 3511, Level = LogLevel.Warning, Message = "ClusterManager: ......Deferred recovery of {Count} executing [DisallowConcurrentExecution] job(s).")]
    public static partial void ExecutingJobRecoveriesDeferred(this ILogger logger, int count);

    [LoggerMessage(EventId = 3512, Level = LogLevel.Information, Message = "ClusterManager: Released {Count} auto-pinned trigger(s) from dead node '{InstanceId}' for re-acquisition.")]
    public static partial void AutoPinnedTriggersReleased(this ILogger logger, int count, string instanceId);

    [LoggerMessage(EventId = 3513, Level = LogLevel.Debug, Message = "Check-in complete.")]
    public static partial void CheckInComplete(this ILogger logger);

    [LoggerMessage(EventId = 3514, Level = LogLevel.Error, Message = "Error managing cluster: {ExceptionMessage}")]
    public static partial void ClusterManagementFailed(this ILogger logger, string exceptionMessage, Exception exception);
}
