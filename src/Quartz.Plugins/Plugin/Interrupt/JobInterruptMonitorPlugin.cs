using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Quartz.Listener;
using Quartz.Diagnostics;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Plugin.Interrupt;

/// <summary>
/// This plugin catches the event of job running for a long time (more than the
/// configured max time) and tells the scheduler to "try" interrupting it if enabled.
/// </summary>
/// <seealso cref="IScheduler.Interrupt(Quartz.JobKey,System.Threading.CancellationToken)"/>
/// <author>Rama Chavali</author>
/// <author>Marko Lahma (.NET)</author>
public class JobInterruptMonitorPlugin : TriggerListenerSupport, ISchedulerPlugin
{
    private const string JobInterruptMonitorKey = "JOB_INTERRUPT_MONITOR_KEY";
    private static readonly TimeSpan defaultMaxRunTime = TimeSpan.FromMinutes(5);

    public const string JobDataMapKeyAutoInterruptable = "AutoInterruptable";
    public const string JobDataMapKeyMaxRunTime = "MaxRunTime";

    private readonly ILogger<JobInterruptMonitorPlugin> logger = LogProvider.CreateLogger<JobInterruptMonitorPlugin>();

    private IScheduler scheduler = null!;
    private string name = null!;
    private QueuedTaskScheduler taskScheduler = null!;

    // active monitors
    private readonly ConcurrentDictionary<string, InterruptMonitor> interruptMonitors = new();

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        taskScheduler.Dispose();
        return default;
    }

    private void ScheduleJobInterruptMonitor(string fireInstanceId, JobKey jobkey, TimeSpan delay)
    {
        var monitor = new InterruptMonitor(fireInstanceId, jobkey, scheduler, delay);
        _ = Task.Factory.StartNew(
            monitor.Run,
            monitor.cancellationTokenSource.Token,
            TaskCreationOptions.HideScheduler,
            taskScheduler).Unwrap();

        interruptMonitors.TryAdd(fireInstanceId, monitor);
    }

    /// <summary>
    /// The amount of time the job is allowed to run before job interruption is signaled.
    /// Defaults to 5 minutes.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan DefaultMaxRunTime { get; set; } = defaultMaxRunTime;

    public override string Name => name;

    public override ValueTask TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Call the scheduleJobInterruptMonitor and capture the ScheduledFuture in context
        try
        {
            // Schedule Monitor only if the job wants AutoInterruptable functionality
            if (context.JobDetail.JobDataMap.TryGetBoolean(JobDataMapKeyAutoInterruptable, out bool value) && value)
            {
                var monitorPlugin = (JobInterruptMonitorPlugin) context.Scheduler.Context[JobInterruptMonitorKey]!;

                // Get the MaxRuntime from MergedJobDataMap if NOT available use MaxRunTime from Plugin Configuration
                var jobDataDelay = DefaultMaxRunTime;

                if (context.MergedJobDataMap.GetString(JobDataMapKeyMaxRunTime) is not null)
                {
                    jobDataDelay = TimeSpan.FromMilliseconds(context.MergedJobDataMap.GetLong(JobDataMapKeyMaxRunTime));
                }

                monitorPlugin.ScheduleJobInterruptMonitor(context.FireInstanceId, context.JobDetail.Key, jobDataDelay);
                logger.LogDebug("Job's Interrupt Monitor has been scheduled to interrupt with the delay :{Delay}", jobDataDelay);
            }
        }
        catch (SchedulerException e)
        {
            logger.LogError(e, "Error scheduling interrupt monitor {ErrorMessage}", e.Message);
        }

        return default;
    }

    public override ValueTask TriggerComplete(
        ITrigger trigger,
        IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        // cancel the interrupt task if job is complete
        if (interruptMonitors.TryRemove(context.FireInstanceId, out var monitor))
        {
            monitor.Cancel();
        }

        return default;
    }

    public ValueTask Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Registering Job Interrupt Monitor Plugin");
        this.name = name;

        taskScheduler = new QueuedTaskScheduler(1, "JobInterruptMonitorPlugin");
        scheduler.Context.Put(JobInterruptMonitorKey, this);
        this.scheduler = scheduler;

        // Set the trigger Listener as this class to the ListenerManager here
        this.scheduler.ListenerManager.AddTriggerListener(this);

        return default;
    }

    private sealed class InterruptMonitor
    {
        private readonly ILogger<InterruptMonitor> logger = LogProvider.CreateLogger<InterruptMonitor>();

        private readonly JobKey jobKey;
        private readonly IScheduler scheduler;
        private readonly TimeSpan delay;

        internal readonly CancellationTokenSource cancellationTokenSource;

        public InterruptMonitor(string fireInstanceId, JobKey jobKey, IScheduler scheduler, TimeSpan delay)
        {
            FireInstanceId = fireInstanceId;
            this.jobKey = jobKey;
            this.scheduler = scheduler;
            this.delay = delay;

            cancellationTokenSource = new CancellationTokenSource();
        }

        public string FireInstanceId { get; }

        public async Task Run()
        {
            try
            {
                await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(false);

                // Interrupt the job here - using Scheduler API that gets propagated to Job's interrupt
                logger.LogInformation("Interrupting Job as it ran more than the configured max time. Job Details [{JobName}:{JobGroup}]", jobKey.Name, jobKey.Group);
                await scheduler.Interrupt(jobKey, cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // OK, run completed before need to cancel
            }
            catch (SchedulerException ex)
            {
                logger.LogError(ex, "Error interrupting Job: {ExceptionMessage}", ex.Message);
            }
        }

        public void Cancel()
        {
            try
            {
                cancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cancelling monitor: {ExceptionMessage}", ex.Message);
            }
        }
    }
}