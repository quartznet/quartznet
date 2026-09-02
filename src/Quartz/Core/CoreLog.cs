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

using Quartz.Extensibility;

namespace Quartz.Core;

/// <summary>
/// Every event the scheduler core logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 1000-1999 belong to this area and are allocated in file order: the scheduler itself
/// (1000-1029), its firing loop (1030-1049), the job run shell (1050-1069), the signaler (1070-1079),
/// the error listener of last resort (1080-1089) and the timeout middleware (1090-1099). An id, once
/// given out, is what an operator
/// filters and alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class CoreLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "JobFactory set to: {Value}")]
    public static partial void JobFactorySet(this ILogger logger, IJobFactory value);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Quartz Scheduler created")]
    public static partial void SchedulerCreated(this ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Scheduler {SchedulerIdentifier} started.")]
    public static partial void SchedulerStarted(this ILogger logger, string schedulerIdentifier);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Unable to start scheduler after startup delay.")]
    public static partial void DelayedStartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Scheduler {SchedulerIdentifier} paused.")]
    public static partial void SchedulerPaused(this ILogger logger, string schedulerIdentifier);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Scheduler {SchedulerIdentifier} shutting down.")]
    public static partial void SchedulerShuttingDown(this ILogger logger, string schedulerIdentifier);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "Scheduler {SchedulerIdentifier} gave up waiting for its running jobs, which are still executing. Their job store updates may not complete, and the store is about to be shut down under them.")]
    public static partial void GaveUpWaitingForRunningJobs(this ILogger logger, string schedulerIdentifier);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Scheduler {SchedulerIdentifier} Shutdown complete.")]
    public static partial void SchedulerShutdownComplete(this ILogger logger, string schedulerIdentifier);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of error")]
    public static partial void ListenerNotificationOfErrorFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "  Original error (for notification) was: {Message}")]
    public static partial void OriginalErrorForNotification(this ILogger logger, string? message, Exception? exception);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of unscheduled job. Trigger={TriggerKey}")]
    public static partial void ListenerNotificationOfUnscheduledJobFailed(this ILogger logger, string triggerKey, Exception exception);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of paused group: {Group}")]
    public static partial void ListenerNotificationOfPausedGroupFailed(this ILogger logger, string? group, Exception exception);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of trigger in error state. Trigger={TriggerKey}")]
    public static partial void ListenerNotificationOfTriggerInErrorFailed(this ILogger logger, TriggerKey triggerKey, Exception exception);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of job triggers in error state. Job={JobKey}")]
    public static partial void ListenerNotificationOfJobTriggersInErrorFailed(this ILogger logger, JobKey jobKey, Exception exception);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of paused trigger. Trigger={TriggerKey}")]
    public static partial void ListenerNotificationOfPausedTriggerFailed(this ILogger logger, TriggerKey triggerKey, Exception exception);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of resumed trigger. Trigger={TriggerKey}")]
    public static partial void ListenerNotificationOfResumedTriggerFailed(this ILogger logger, TriggerKey triggerKey, Exception exception);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of paused job. Job={JobKey}")]
    public static partial void ListenerNotificationOfPausedJobFailed(this ILogger logger, JobKey jobKey, Exception exception);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of resumed job: {JobKey}")]
    public static partial void ListenerNotificationOfResumedJobFailed(this ILogger logger, JobKey jobKey, Exception exception);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of resumed group: {Group}")]
    public static partial void ListenerNotificationOfResumedGroupFailed(this ILogger logger, string group, Exception exception);

    [LoggerMessage(EventId = 1019, Level = LogLevel.Error, Message = "Error while notifying SchedulerListener of {Action}")]
    public static partial void ListenerNotificationFailed(this ILogger logger, string action, Exception exception);

    // Warning, and both settings named, because this is a shutdown that abandoned work rather than one
    // that finished it -- and the two settings are the only things that decide whether it did.
    [LoggerMessage(EventId = 1020, Level = LogLevel.Warning, Message = "Scheduler {SchedulerIdentifier} is shutting down with {ExecutingJobCount} job(s) still executing, and is not waiting for them. Pass waitForJobsToComplete: true (or set QuartzHostedServiceOptions.WaitForJobsToComplete) to let them finish; ShutdownJobInterruption is {ShutdownJobInterruption}, which decides whether they are asked to stop rather than simply abandoned.")]
    public static partial void ShuttingDownWithJobsStillExecuting(this ILogger logger, string schedulerIdentifier, int executingJobCount, ShutdownJobInterruption shutdownJobInterruption);

    // Warning, because the subscriber is attached and producing nothing: a deployment that added
    // OpenTelemetry.Instrumentation.Quartz and sees no spans has no other way to find that out.
    [LoggerMessage(EventId = 1021, Level = LogLevel.Warning, Message = "Something is subscribed to the DiagnosticListener named '{DiagnosticListenerName}', which Quartz 3.x published on and 4.x does not. Nothing will arrive there. 4.x emits spans on ActivitySource(\"{ActivitySourceName}\") and metrics on Meter(\"{MeterName}\") -- subscribe with AddSource(QuartzInstrumentation.ActivitySourceName) and AddMeter(QuartzInstrumentation.MeterName), and drop OpenTelemetry.Instrumentation.Quartz, which emits nothing here.")]
    public static partial void LegacyDiagnosticListenerSubscribed(this ILogger logger, string diagnosticListenerName, string activitySourceName, string meterName);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Debug, Message = "Batch acquisition of {TriggerCount} triggers")]
    public static partial void TriggerBatchAcquired(this ILogger logger, int triggerCount);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Error, Message = "quartzSchedulerThreadLoop: RuntimeException {Message}")]
    public static partial void SchedulerThreadLoopFailed(this ILogger logger, string message, Exception exception);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Error, Message = "DbException while firing trigger {Trigger}")]
    public static partial void TriggerFireFailedWithDbException(this ILogger logger, IOperableTrigger trigger, Exception exception);

    [LoggerMessage(EventId = 1033, Level = LogLevel.Debug, Message = "ThreadPool.TryRun() returned false due to scheduler shutdown, completing trigger")]
    public static partial void ThreadPoolRefusedWorkDuringShutdown(this ILogger logger);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Error, Message = "ThreadPool.TryRun() returned false")]
    public static partial void ThreadPoolRefusedWork(this ILogger logger);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error, Message = "Runtime error occurred in main trigger firing loop.")]
    public static partial void TriggerFiringLoopFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Error, Message = "Error releasing acquired trigger '{TriggerKey}' {Context}")]
    public static partial void AcquiredTriggerReleaseFailed(this ILogger logger, TriggerKey triggerKey, string context, Exception exception);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Debug, Message = "Calling Execute on job {JobKey}")]
    public static partial void JobExecuting(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 1051, Level = LogLevel.Information, Message = "Job {JobDetailKey} was cancelled")]
    public static partial void JobCancelled(this ILogger logger, JobKey jobDetailKey);

    [LoggerMessage(EventId = 1052, Level = LogLevel.Error, Message = "Job {JobDetailKey} threw a JobExecutionException: ")]
    public static partial void JobThrewJobExecutionException(this ILogger logger, JobKey jobDetailKey, Exception exception);

    [LoggerMessage(EventId = 1053, Level = LogLevel.Error, Message = "Job {JobDetailKey} threw an unhandled Exception: ")]
    public static partial void JobThrewUnhandledException(this ILogger logger, JobKey jobDetailKey, Exception exception);

    [LoggerMessage(EventId = 1054, Level = LogLevel.Debug, Message = "Trigger instruction : {InstructionCode}")]
    public static partial void TriggerInstructionDecided(this ILogger logger, SchedulerInstruction instructionCode);

    [LoggerMessage(EventId = 1055, Level = LogLevel.Debug, Message = "Rescheduling trigger to reexecute")]
    public static partial void TriggerRefiring(this ILogger logger);

    [LoggerMessage(EventId = 1056, Level = LogLevel.Information, Message = "Job of trigger {TriggerKey} failed; retry {Attempt} of {MaxAttempts} scheduled for {RetryTimeUtc}")]
    public static partial void TriggerRetryScheduled(this ILogger logger, TriggerKey triggerKey, int attempt, int maxAttempts, DateTimeOffset retryTimeUtc);

    [LoggerMessage(EventId = 1070, Level = LogLevel.Information, Message = "Initialized Scheduler Signaller of type: {Type}")]
    public static partial void SchedulerSignalerInitialized(this ILogger logger, Type type);

    [LoggerMessage(EventId = 1071, Level = LogLevel.Error, Message = "Error notifying listeners of trigger misfire.")]
    public static partial void ListenerNotificationOfMisfireFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1080, Level = LogLevel.Error, Message = "{Message} (scheduler: {SchedulerName})")]
    public static partial void SchedulerError(this ILogger logger, string? message, string schedulerName, Exception? exception);

    [LoggerMessage(EventId = 1081, Level = LogLevel.Error, Message = "{Message} (scheduler: {SchedulerName}, trigger: {TriggerKey}, job: {JobKey}, fire instance: {FireInstanceId})")]
    public static partial void SchedulerErrorForFire(
        this ILogger logger,
        string? message,
        string schedulerName,
        TriggerKey? triggerKey,
        JobKey? jobKey,
        string? fireInstanceId,
        Exception? exception);

    [LoggerMessage(EventId = 1090, Level = LogLevel.Warning, Message = "Job {JobKey} exceeded the {Budget} it was allowed; interrupting fire instance {FireInstanceId}")]
    public static partial void JobTimedOut(this ILogger logger, JobKey jobKey, TimeSpan budget, string fireInstanceId);

    [LoggerMessage(EventId = 1091, Level = LogLevel.Debug, Message = "Job {JobKey} finished before the interrupt its timeout asked for reached fire instance {FireInstanceId}")]
    public static partial void JobTimeoutFoundNothingToInterrupt(this ILogger logger, JobKey jobKey, string fireInstanceId);

    [LoggerMessage(EventId = 1092, Level = LogLevel.Error, Message = "Interrupting timed out job {JobKey}, fire instance {FireInstanceId}, failed")]
    public static partial void JobTimeoutInterruptFailed(this ILogger logger, JobKey jobKey, string fireInstanceId, Exception exception);
}
