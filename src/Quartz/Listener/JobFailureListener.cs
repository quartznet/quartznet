using System;
using System.Threading;
using System.Threading.Tasks;
using Quartz.Logging;

namespace Quartz.Listener
{
    public class JobFailureListener : IJobListener
    {
        private const string IntervalKey = nameof(IntervalKey);
        private const string MaxRetriesKey = nameof(MaxRetriesKey);
        private const string NumTriesKey = nameof(NumTriesKey);
        private const string FailureTriggerGroup = nameof(FailureTriggerGroup);
        private readonly ILog log;

        public string Name => nameof(JobFailureListener);

        public JobFailureListener()
        {
            log = LogProvider.GetLogger(GetType());
        }

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            var jobDataMap = context.JobDetail.JobDataMap;

            if (!jobDataMap.Contains(MaxRetriesKey))
            {
                var retryAttributes = context.JobDetail
                                             .JobType
                                             .GetCustomAttributes(typeof(RetryAttribute), true);
                if (retryAttributes.Length > 0)
                {
                    var retryAttribute = (RetryAttribute)retryAttributes[0];
                    jobDataMap.Put(MaxRetriesKey, retryAttribute.MaxRetries);
                    jobDataMap.Put(IntervalKey, retryAttribute.Interval);
                }
            }

            if (!jobDataMap.Contains(NumTriesKey))
            {
                jobDataMap.Put(NumTriesKey, 0);
            }

            var numberTries = jobDataMap.GetIntValue(NumTriesKey);
            jobDataMap.Put(NumTriesKey, ++numberTries);

            return Task.CompletedTask;
        }

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            if (jobException is null)
            {
                return;
            }

            var jobDataMap = context.JobDetail.JobDataMap;
            var numTries = jobDataMap.GetIntValue(NumTriesKey);
            var maxRetries = jobDataMap.GetIntValue(MaxRetriesKey);
            var interval = jobDataMap.GetIntValue(IntervalKey);

            if (numTries > maxRetries)
            {
                log.InfoFormat("Job with ID and type: {Key}, {JobType} has run {maxRetries} times and has failed each time.", context.JobDetail.Key, context.JobDetail.JobType, maxRetries);

                return;
            }

            var trigger = TriggerBuilder.Create()
                                        .WithIdentity(Guid.NewGuid().ToString(), FailureTriggerGroup)
                                        .StartAt(DateTime.Now.AddSeconds(interval * numTries))
                                        .Build();

            log.InfoFormat("Job with ID and type: {Key}, {JobType} has thrown the exception: {Exception}.Running again in {Time} seconds.", context.JobDetail.Key, context.JobDetail.JobType, jobException, interval * numTries);

            await context.Scheduler.RescheduleJob(context.Trigger.Key, trigger);
        }
    }
}
