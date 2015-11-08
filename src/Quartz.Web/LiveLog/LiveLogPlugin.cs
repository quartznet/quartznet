using System;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Quartz.Util;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.LiveLog
{
    public class LiveLogPlugin : ITriggerListener, IJobListener, ISchedulerListener
    {
        public LiveLogPlugin()
        {
            Name = "Quartz Live Logs Plugin";
        }

        public string Name { get; }

        public Task JobToBeExecutedAsync(IJobExecutionContext context)
        {
            return SendToClients(x => x.jobToBeExecuted(new KeyDto(context.JobDetail.Key), new KeyDto(context.Trigger.Key)));
        }

        public Task JobExecutionVetoedAsync(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobWasExecutedAsync(IJobExecutionContext context, JobExecutionException jobException)
        {
            return SendToClients(x => x.jobWasExecuted(new KeyDto(context.JobDetail.Key), new KeyDto(context.Trigger.Key), jobException?.Message));
        }

        public Task TriggerFiredAsync(ITrigger trigger, IJobExecutionContext context)
        {
            return SendToClients(x => x.triggerFired(new KeyDto(trigger.Key)));
        }

        public Task<bool> VetoJobExecutionAsync(ITrigger trigger, IJobExecutionContext context)
        {
            return Task.FromResult(false);
        }

        public Task TriggerMisfiredAsync(ITrigger trigger)
        {
            return SendToClients(x => x.triggerMisfired(new KeyDto(trigger.Key)));
        }

        public Task TriggerCompleteAsync(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode)
        {
            return SendToClients(x => x.triggerComplete(new KeyDto(trigger.Key)));
        }

        public Task JobScheduledAsync(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobUnscheduledAsync(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerFinalizedAsync(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerPausedAsync(TriggerKey triggerKey)
        {
            return SendToClients(x => x.triggerPaused(new KeyDto(triggerKey)));
        }

        public Task TriggersPausedAsync(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerResumedAsync(TriggerKey triggerKey)
        {
            return SendToClients(x => x.triggerResumed(new KeyDto(triggerKey)));
        }

        public Task TriggersResumedAsync(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobAddedAsync(IJobDetail jobDetail)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobDeletedAsync(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobPausedAsync(JobKey jobKey)
        {
            return SendToClients(x => x.jobPaused(jobKey));
        }

        public Task JobsPausedAsync(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobResumedAsync(JobKey jobKey)
        {
            return SendToClients(x => x.jobResumed(jobKey));
        }

        public Task JobsResumedAsync(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerErrorAsync(string msg, SchedulerException cause)
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerInStandbyModeAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerStartedAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerStartingAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerShutdownAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerShuttingdownAsync()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulingDataClearedAsync()
        {
            return TaskUtil.CompletedTask;
        }

        private Task SendToClients(Action<dynamic> action)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext<LiveLogHub>();
            action(context.Clients.All);
            return TaskUtil.CompletedTask;
        }
    }
}