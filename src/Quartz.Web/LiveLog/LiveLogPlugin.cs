/*using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
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

        public Task JobToBeExecuted(IJobExecutionContext context)
        {
            return SendToClients(x => x.jobToBeExecuted(new KeyDto(context.JobDetail.Key), new KeyDto(context.Trigger.Key)));
        }

        public Task JobExecutionVetoed(IJobExecutionContext context)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            return SendToClients(x => x.jobWasExecuted(new KeyDto(context.JobDetail.Key), new KeyDto(context.Trigger.Key), jobException?.Message));
        }

        public Task TriggerFired(ITrigger trigger, IJobExecutionContext context)
        {
            return SendToClients(x => x.triggerFired(new KeyDto(trigger.Key)));
        }

        public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context)
        {
            return Task.FromResult(false);
        }

        public Task TriggerMisfired(ITrigger trigger)
        {
            return SendToClients(x => x.triggerMisfired(new KeyDto(trigger.Key)));
        }

        public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode)
        {
            return SendToClients(x => x.triggerComplete(new KeyDto(trigger.Key)));
        }

        public Task JobScheduled(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobUnscheduled(TriggerKey triggerKey)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerFinalized(ITrigger trigger)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerPaused(TriggerKey triggerKey)
        {
            return SendToClients(x => x.triggerPaused(new KeyDto(triggerKey)));
        }

        public Task TriggersPaused(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task TriggerResumed(TriggerKey triggerKey)
        {
            return SendToClients(x => x.triggerResumed(new KeyDto(triggerKey)));
        }

        public Task TriggersResumed(string triggerGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobAdded(IJobDetail jobDetail)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobDeleted(JobKey jobKey)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobPaused(JobKey jobKey)
        {
            return SendToClients(x => x.jobPaused(jobKey));
        }

        public Task JobsPaused(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task JobResumed(JobKey jobKey)
        {
            return SendToClients(x => x.jobResumed(jobKey));
        }

        public Task JobsResumed(string jobGroup)
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerError(string msg, SchedulerException cause)
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerInStandbyMode()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerStarted()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerStarting()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerShutdown()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulerShuttingdown()
        {
            return TaskUtil.CompletedTask;
        }

        public Task SchedulingDataCleared()
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
*/