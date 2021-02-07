using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Listener;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Plugin.Interrupt
{
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
            var monitor = new InterruptMonitor(fireInstanceId, jobkey, scheduler, delay);
            Task.Factory.StartNew(
                () => monitor.Run().ContinueWith(_ =>
                {
                    interruptMonitors.TryRemove(monitor.FireInstanceId, out var m);
                }),
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

        public override Task TriggerFired(
            ITrigger trigger, 
            IJobExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            // Call the scheduleJobInterruptMonitor and capture the ScheduledFuture in context
            try
            {
                // Schedule Monitor only if the job wants AutoInterruptable functionality
                if (context.JobDetail.JobDataMap.GetBoolean(JobDataMapKeyAutoInterruptable))
                {
                    JobInterruptMonitorPlugin monitorPlugin = (JobInterruptMonitorPlugin) context.Scheduler.Context.Get(JobInterruptMonitorKey);
                    // Get the MaxRuntime from Job Data if NOT available use MaxRunTime from Plugin Configuration
                    var jobDataDelay = DefaultMaxRunTime;

                    if (context.JobDetail.JobDataMap.GetString(JobDataMapKeyMaxRunTime) != null)
                    {
                        jobDataDelay = TimeSpan.FromMilliseconds(context.JobDetail.JobDataMap.GetLongValueFromString(JobDataMapKeyMaxRunTime));
                    }

                    monitorPlugin.ScheduleJobInterruptMonitor(context.FireInstanceId, context.JobDetail.Key, jobDataDelay);
                    log.Debug("Job's Interrupt Monitor has been scheduled to interrupt with the delay :"+ jobDataDelay);
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
            if (interruptMonitors.TryGetValue(context.FireInstanceId, out var monitor))
            {
                try
                {
                    monitor.Cancel();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
            
            return Task.CompletedTask;
        }

        public Task Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            log.Info("Registering Job Interrupt Monitor Plugin");
            this.name = name;

            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadName: "JobInterruptMonitorPlugin");
            scheduler.Context.Put(JobInterruptMonitorKey, this);
            this.scheduler = scheduler;

            // Set the trigger Listener as this class to the ListenerManager here
            this.scheduler.ListenerManager.AddTriggerListener(this);
            
            return Task.CompletedTask;
        }

        private sealed class InterruptMonitor
        {
            private readonly ILog log = LogProvider.GetLogger(typeof(InterruptMonitor));

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
                    await Task.Delay(delay, cancellationTokenSource.Token);

                    // Interrupt the job here - using Scheduler API that gets propagated to Job's interrupt
                    log.Info($"Interrupting Job as it ran more than the configured max time. Job Details [{jobKey.Name}:{jobKey.Group}]");
                    await scheduler.Interrupt(jobKey, cancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    // OK, run completed before need to cancel
                } 
                catch (SchedulerException ex)
                {
                    log.Error($"Error interrupting Job: {ex.Message}", ex);
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
}