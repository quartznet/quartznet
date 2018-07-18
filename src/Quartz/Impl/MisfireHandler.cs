using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl
{
    internal class MisfireHandler
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (MisfireHandler));
        // keep constant lock requestor id for handler's lifetime
        private readonly Guid requestorId = Guid.NewGuid();

        private readonly TimeSpan retryInterval;
        private readonly int retryableActionErrorLogThreshold;
        private readonly ISchedulerSignaler schedulerSignaler;
        private readonly IMisfireHandlerOperations operations;

        private int numFails;

        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly QueuedTaskScheduler taskScheduler;
        private Task task;

        internal MisfireHandler(
            string instanceId,
            string instanceName,
            bool makeThreadDaemon,
            TimeSpan retryInterval,
            int retryableActionErrorLogThreshold,
            ISchedulerSignaler schedulerSignaler,
            IMisfireHandlerOperations operations)
        {
            this.retryInterval = retryInterval;
            this.retryableActionErrorLogThreshold = retryableActionErrorLogThreshold;
            this.schedulerSignaler = schedulerSignaler;
            this.operations = operations;

            string threadName = $"QuartzScheduler_{instanceName}-{instanceId}_MisfireHandler";
            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadName: threadName, useForegroundThreads: !makeThreadDaemon);
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
                    await schedulerSignaler.SignalSchedulingChange(recoverMisfiredJobsResult.EarliestNewTime, CancellationToken.None).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();

                TimeSpan timeToSleep = TimeSpan.FromMilliseconds(50); // At least a short pause to help balance threads
                if (!recoverMisfiredJobsResult.HasMoreMisfiredTriggers)
                {
                    timeToSleep = operations.MisfireHandlerFrequency - (SystemTime.UtcNow() - sTime);
                    if (timeToSleep <= TimeSpan.Zero)
                    {
                        timeToSleep = TimeSpan.FromMilliseconds(50);
                    }

                    if (numFails > 0)
                    {
                        timeToSleep = retryInterval > timeToSleep ? retryInterval : timeToSleep;
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
                var result = await operations.RecoverMisfires(requestorId, CancellationToken.None).ConfigureAwait(false);
                numFails = 0;
                return result;
            }
            catch (Exception e)
            {
                if (numFails%retryableActionErrorLogThreshold == 0)
                {
                    log.ErrorException("Error handling misfires: " + e.Message, e);
                }
                numFails++;
            }
            return RecoverMisfiredJobsResult.NoOp;
        }
    }
}