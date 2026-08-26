using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Listeners;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Plugins.Interrupt;

/// <summary>
/// This plugin catches the event of job running for a long time (more than the
/// configured max time) and tells the scheduler to "try" interrupting it if enabled.
/// </summary>
/// <seealso cref="IScheduler.InterruptFireInstance(string,System.Threading.CancellationToken)"/>
/// <author>Rama Chavali</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class JobInterruptMonitorPlugin : ITriggerListener, ISchedulerPlugin
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
        var monitor = new InterruptMonitor(this, fireInstanceId, jobkey, scheduler, delay);
        if (!interruptMonitors.TryAdd(fireInstanceId, monitor))
        {
            // a re-executed job (SchedulerInstruction.ReExecuteJob) fires trigger listeners again
            // with the same fire instance id - the existing monitor keeps watching it
            return;
        }

        _ = Task.Factory.StartNew(
            monitor.Run,
            monitor.cancellationTokenSource.Token,
            TaskCreationOptions.HideScheduler,
            taskScheduler).Unwrap();
    }

    /// <summary>
    /// The amount of time the job is allowed to run before job interruption is signaled.
    /// Defaults to 5 minutes.
    /// </summary>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan DefaultMaxRunTime { get; internal set; } = defaultMaxRunTime;

    public string Name => name;

    public ValueTask TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Call the scheduleJobInterruptMonitor and capture the ScheduledFuture in context
        try
        {
            // Schedule Monitor only if the job wants AutoInterruptable functionality
            if (context.MergedJobDataMap.TryGetBoolean(JobDataMapKeyAutoInterruptable, out bool value) && value)
            {
                var monitorPlugin = (JobInterruptMonitorPlugin) context.Scheduler.Context[JobInterruptMonitorKey]!;

                // Get the MaxRuntime from MergedJobDataMap if NOT available use MaxRunTime from Plugin Configuration.
                // TryGetLong coerces a numeric value and a numeric string alike, so the override takes
                // effect whether the map holds 5000 or "5000".
                TimeSpan jobDataDelay = DefaultMaxRunTime;

                if (context.MergedJobDataMap.TryGetLong(JobDataMapKeyMaxRunTime, out long maxRunTimeMilliseconds))
                {
                    jobDataDelay = TimeSpan.FromMilliseconds(maxRunTimeMilliseconds);
                }
                else if (context.MergedJobDataMap.ContainsKey(JobDataMapKeyMaxRunTime))
                {
                    logger.MaxRunTimeNotANumber(JobDataMapKeyMaxRunTime, jobDataDelay);
                }

                monitorPlugin.ScheduleJobInterruptMonitor(context.FireInstanceId, context.JobDetail.Key, jobDataDelay);
                logger.InterruptMonitorScheduled(jobDataDelay);
            }
        }
        catch (SchedulerException e)
        {
            logger.InterruptMonitorSchedulingFailed(e.Message, e);
        }

        return default;
    }

    public ValueTask TriggerComplete(
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

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        logger.PluginRegistered();
        this.name = pluginName;

        taskScheduler = new QueuedTaskScheduler(1, "JobInterruptMonitorPlugin");
        scheduler.Context[JobInterruptMonitorKey] = this;
        this.scheduler = scheduler;

        // Set the trigger Listener as this class to the ListenerManager here
        this.scheduler.ListenerManager.AddTriggerListener(this);

        // a vetoed execution never reaches TriggerComplete, so its monitor must be cancelled from a job listener
        this.scheduler.ListenerManager.AddJobListener(new JobExecutionVetoedListener(this));

        return default;
    }

    private sealed class JobExecutionVetoedListener : IJobListener
    {
        private readonly JobInterruptMonitorPlugin plugin;

        public JobExecutionVetoedListener(JobInterruptMonitorPlugin plugin)
        {
            this.plugin = plugin;
        }

        public string Name => plugin.name + "-VetoedJobListener";

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (plugin.interruptMonitors.TryRemove(context.FireInstanceId, out var monitor))
            {
                monitor.Cancel();
            }

            return default;
        }
    }

    private sealed class InterruptMonitor
    {
        private readonly ILogger<InterruptMonitor> logger = LogProvider.CreateLogger<InterruptMonitor>();

        private readonly JobInterruptMonitorPlugin plugin;
        private readonly JobKey jobKey;
        private readonly IScheduler scheduler;
        private readonly TimeSpan delay;

        internal readonly CancellationTokenSource cancellationTokenSource;

        public InterruptMonitor(JobInterruptMonitorPlugin plugin, string fireInstanceId, JobKey jobKey, IScheduler scheduler, TimeSpan delay)
        {
            this.plugin = plugin;
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

                // Interrupt the job here - using Scheduler API that gets propagated to Job's interrupt.
                // Interrupting by fire instance id makes sure only the monitored execution is signaled
                // and not every currently running execution of the same job.
                bool interrupted = await scheduler.InterruptFireInstance(FireInstanceId, cancellationTokenSource.Token).ConfigureAwait(false);
                if (interrupted)
                {
                    logger.JobInterrupted(jobKey.Name, jobKey.Group, FireInstanceId);
                }
                else
                {
                    logger.JobNoLongerRunning(jobKey.Name, jobKey.Group, FireInstanceId);
                }
            }
            catch (TaskCanceledException)
            {
                // OK, run completed before need to cancel
            }
            catch (SchedulerException ex)
            {
                logger.JobInterruptFailed(ex.Message, ex);
            }
            finally
            {
                // TriggerComplete and JobExecutionVetoed already remove the entry; this bounds the
                // dictionary when neither notification arrives, e.g. a listener earlier in the chain threw
                plugin.interruptMonitors.TryRemove(FireInstanceId, out _);
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
                logger.MonitorCancellationFailed(ex.Message, ex);
            }
        }
    }
}