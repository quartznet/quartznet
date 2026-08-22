using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Listener;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Plugin.Interrupt;

/// <summary>
/// This plugin catches the event of job running for a long time (more than the
/// configured max time) and tells the scheduler to "try" interrupting it if enabled.
/// </summary>
/// <seealso cref="IScheduler.Interrupt(string,System.Threading.CancellationToken)"/>
/// <author>Rama Chavali</author>
/// <author>Marko Lahma (.NET)</author>
public class JobInterruptMonitorPlugin : TriggerListenerSupport, ISchedulerPlugin
{
    private const string JobInterruptMonitorKey = "JOB_INTERRUPT_MONITOR_KEY";
    private static readonly TimeSpan defaultMaxRunTime = TimeSpan.FromMinutes(5);

    public const string JobDataMapKeyAutoInterruptable = "AutoInterruptable";
    public const string JobDataMapKeyMaxRunTime = "MaxRunTime";

    private ILog log = LogProvider.GetLogger(typeof(JobInterruptMonitorPlugin));

    private IScheduler scheduler = null!;
    private string name = null!;
    private QueuedTaskScheduler taskScheduler = null!;

    // active monitors
    private ConcurrentDictionary<string, InterruptMonitor> interruptMonitors = new();

    public Task Start(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Shutdown(CancellationToken cancellationToken = default)
    {
        taskScheduler.Dispose();
        return Task.CompletedTask;
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

        Task.Factory.StartNew(
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
    public TimeSpan DefaultMaxRunTime { get; set; } = defaultMaxRunTime;

    public override string Name => name;

    public override Task TriggerFired(
        ITrigger trigger,
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Call the scheduleJobInterruptMonitor and capture the ScheduledFuture in context
        try
        {
            // Schedule Monitor only if the job wants AutoInterruptable functionality
            if (context.MergedJobDataMap.TryGetBoolean(JobDataMapKeyAutoInterruptable, out var value) && value)
            {
                JobInterruptMonitorPlugin monitorPlugin = (JobInterruptMonitorPlugin) context.Scheduler.Context.Get(JobInterruptMonitorKey);
                // Get the MaxRuntime from MergedJobDataMap if NOT available use MaxRunTime from Plugin Configuration.
                // TryGetLong coerces a numeric value and a numeric string alike, so the override takes
                // effect whether the map holds 5000 or "5000".
                var jobDataDelay = DefaultMaxRunTime;

                if (context.MergedJobDataMap.TryGetLong(JobDataMapKeyMaxRunTime, out var maxRunTimeMilliseconds))
                {
                    jobDataDelay = TimeSpan.FromMilliseconds(maxRunTimeMilliseconds);
                }
                else if (context.MergedJobDataMap.ContainsKey(JobDataMapKeyMaxRunTime))
                {
                    log.Warn($"Job data map value for {JobDataMapKeyMaxRunTime} is not a number of milliseconds, using the plugin default of {jobDataDelay} instead");
                }

                monitorPlugin.ScheduleJobInterruptMonitor(context.FireInstanceId, context.JobDetail.Key, jobDataDelay);
                log.Debug("Job's Interrupt Monitor has been scheduled to interrupt with the delay :" + jobDataDelay);
            }
        }
        catch (SchedulerException e)
        {
            log.Info($"Error scheduling interrupt monitor {e.Message}", e);
        }

        return Task.CompletedTask;
    }

    public override Task TriggerComplete(
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

        return Task.CompletedTask;
    }

    public Task Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        log.Info("Registering Job Interrupt Monitor Plugin");
        this.name = name;

        taskScheduler = new QueuedTaskScheduler(1, "JobInterruptMonitorPlugin");
        scheduler.Context.Put(JobInterruptMonitorKey, this);
        this.scheduler = scheduler;

        // Set the trigger Listener as this class to the ListenerManager here
        this.scheduler.ListenerManager.AddTriggerListener(this);

        // a vetoed execution never reaches TriggerComplete, so its monitor must be cancelled from a job listener
        this.scheduler.ListenerManager.AddJobListener(new JobExecutionVetoedListener(this));

        return Task.CompletedTask;
    }

    private sealed class JobExecutionVetoedListener : JobListenerSupport
    {
        private readonly JobInterruptMonitorPlugin plugin;

        public JobExecutionVetoedListener(JobInterruptMonitorPlugin plugin)
        {
            this.plugin = plugin;
        }

        public override string Name => plugin.name + "-VetoedJobListener";

        public override Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (plugin.interruptMonitors.TryRemove(context.FireInstanceId, out var monitor))
            {
                monitor.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InterruptMonitor
    {
        private readonly ILog log = LogProvider.GetLogger(typeof(InterruptMonitor));

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
                await Task.Delay(delay, cancellationTokenSource.Token);

                // Interrupt the job here - using Scheduler API that gets propagated to Job's interrupt.
                // Interrupting by fire instance id makes sure only the monitored execution is signaled
                // and not every currently running execution of the same job.
                var interrupted = await scheduler.Interrupt(FireInstanceId, cancellationTokenSource.Token);
                if (interrupted)
                {
                    log.Info($"Interrupted Job as it ran more than the configured max time. Job Details [{jobKey.Name}:{jobKey.Group}], fire instance id {FireInstanceId}");
                }
                else
                {
                    log.Debug($"Job execution was no longer running, nothing to interrupt. Job Details [{jobKey.Name}:{jobKey.Group}], fire instance id {FireInstanceId}");
                }
            }
            catch (TaskCanceledException)
            {
                // OK, run completed before need to cancel
            }
            catch (SchedulerException ex)
            {
                log.Error($"Error interrupting Job: {ex.Message}", ex);
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
                log.Error($"Error cancelling monitor: {ex.Message}", ex);
            }
        }
    }
}