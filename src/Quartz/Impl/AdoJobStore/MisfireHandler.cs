using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    internal class MisfireHandler
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (MisfireHandler));
        // keep constant lock requestor id for handler's lifetime
        private readonly Guid requestorId = Guid.NewGuid();

        private readonly JobStoreSupport jobStoreSupport;
        private int numFails;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly QueuedTaskScheduler taskScheduler;
        private Task task;

        internal MisfireHandler(JobStoreSupport jobStoreSupport)
        {
            this.jobStoreSupport = jobStoreSupport;

            string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_MisfireHandler";
            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadName: threadName, useForegroundThreads: !jobStoreSupport.MakeThreadsDaemons);
            cancellationTokenSource = new CancellationTokenSource();
        }

        public virtual void Initialize()
        {
            task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.HideScheduler, taskScheduler).Unwrap();
        }

        private async Task Run()
        {
            var token = cancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                DateTimeOffset sTime = SystemTime.UtcNow();

                RecoverMisfiredJobsResult recoverMisfiredJobsResult = await Manage().ConfigureAwait(false);

                if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
                {
                    jobStoreSupport.SignalSchedulingChangeImmediately(recoverMisfiredJobsResult.EarliestNewTime);
                }

                token.ThrowIfCancellationRequested();

                TimeSpan timeToSleep = TimeSpan.FromMilliseconds(50); // At least a short pause to help balance threads
                if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
                {
                    timeToSleep = jobStoreSupport.MisfireHandlerFrequency - (SystemTime.UtcNow() - sTime);
                    if (timeToSleep <= TimeSpan.Zero)
                    {
                        timeToSleep = TimeSpan.FromMilliseconds(50);
                    }

                    if (numFails > 0)
                    {
                        timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
                    }
                }

                await Task.Delay(timeToSleep, token).ConfigureAwait(false);
            }
        }

        public virtual async Task Shutdown()
        {
            cancellationTokenSource.Cancel();
            try
            {
                taskScheduler.Dispose();
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task<RecoverMisfiredJobsResult> Manage()
        {
            try
            {
                log.Debug("Scanning for misfires...");

                RecoverMisfiredJobsResult res = await jobStoreSupport.DoRecoverMisfires(requestorId, CancellationToken.None).ConfigureAwait(false);
                numFails = 0;
                return res;
            }
            catch (Exception e)
            {
                if (numFails%jobStoreSupport.RetryableActionErrorLogThreshold == 0)
                {
                    log.ErrorException("Error handling misfires: " + e.Message, e);
                }
                numFails++;
            }
            return RecoverMisfiredJobsResult.NoOp;
        }
    }
}