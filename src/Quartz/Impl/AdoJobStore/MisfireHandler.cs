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
        private readonly JobStoreSupport jobStoreSupport;
        private int numFails;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly QueuedTaskScheduler taskScheduler;
        private Task<Task> task;

        internal MisfireHandler(JobStoreSupport jobStoreSupport)
        {
            this.jobStoreSupport = jobStoreSupport;

            string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_MisfireHandler";
            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadName: threadName, useForegroundThreads: !jobStoreSupport.MakeThreadsDaemons);
            cancellationTokenSource = new CancellationTokenSource();
        }

        public virtual void Initialize()
        {
            task = Task.Factory.StartNew(() => RunAsync(), CancellationToken.None, TaskCreationOptions.HideScheduler, taskScheduler);
        }

        private async Task RunAsync()
        {
            CancellationToken token = cancellationTokenSource.Token;
            while (true)
            {
                token.ThrowIfCancellationRequested();

                DateTimeOffset sTime = SystemTime.UtcNow();

                RecoverMisfiredJobsResult recoverMisfiredJobsResult = await ManageAsync().ConfigureAwait(false);

                if (recoverMisfiredJobsResult.ProcessedMisfiredTriggerCount > 0)
                {
                    jobStoreSupport.SignalSchedulingChangeImmediately(recoverMisfiredJobsResult.EarliestNewTime);
                }

                token.ThrowIfCancellationRequested();

                TimeSpan timeToSleep = TimeSpan.FromMilliseconds(50); // At least a short pause to help balance threads
                if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
                {
                    timeToSleep = jobStoreSupport.MisfireThreshold - (SystemTime.UtcNow() - sTime);
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
            // ReSharper disable once FunctionNeverReturns
        }

        public virtual async Task ShutdownAsync()
        {
            cancellationTokenSource.Cancel();
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task<RecoverMisfiredJobsResult> ManageAsync()
        {
            try
            {
                log.Debug("Scanning for misfires...");

                RecoverMisfiredJobsResult res = await jobStoreSupport.DoRecoverMisfires().ConfigureAwait(false);
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