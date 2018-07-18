using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl
{
    internal class ClusterManager
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(ClusterManager));

        // keep constant lock requestor id for manager's lifetime
        private readonly Guid requestorId = Guid.NewGuid();

        private readonly TimeSpan retryInterval;
        private readonly int retryableActionErrorLogThreshold;
        private readonly TimeSpan checkInterval;
        private readonly ISchedulerSignaler schedulerSignaler;
        private readonly IClusterManagementOperations operations;

        private readonly QueuedTaskScheduler taskScheduler;
        private readonly CancellationTokenSource cancellationTokenSource;
        private Task task;

        private int numFails;

        internal ClusterManager(
            string instanceId,
            string instanceName,
            bool makeThreadDaemon,
            TimeSpan retryInterval,
            int retryableActionErrorLogThreshold,
            TimeSpan checkInterval,
            ISchedulerSignaler schedulerSignaler,
            IClusterManagementOperations operations)
        {
            this.retryInterval = retryInterval;
            this.retryableActionErrorLogThreshold = retryableActionErrorLogThreshold;
            this.checkInterval = checkInterval;
            this.schedulerSignaler = schedulerSignaler;
            this.operations = operations;

            string threadName = $"QuartzScheduler_{instanceName}-{instanceId}_ClusterManager";

            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadPriority: ThreadPriority.AboveNormal, threadName: threadName, useForegroundThreads: !makeThreadDaemon);
            cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task Initialize()
        {
            await Manage().ConfigureAwait(false);
            task = Task.Factory.StartNew(() => Run(cancellationTokenSource.Token), cancellationTokenSource.Token, TaskCreationOptions.HideScheduler, taskScheduler).Unwrap();
        }

        public async Task Shutdown()
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

        private async Task<bool> Manage()
        {
            bool res = false;
            try
            {
                res = await operations.CheckCluster(requestorId).ConfigureAwait(false);

                numFails = 0;
                log.Debug("Check-in complete.");
            }
            catch (Exception e)
            {
                if (numFails % retryableActionErrorLogThreshold == 0)
                {
                    log.ErrorException("Error managing cluster: " + e.Message, e);
                }
                numFails++;
            }
            return res;
        }

        private async Task Run(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                TimeSpan timeToSleep = checkInterval;
                TimeSpan transpiredTime = SystemTime.UtcNow() - operations.LastCheckin;
                timeToSleep = timeToSleep - transpiredTime;
                if (timeToSleep <= TimeSpan.Zero)
                {
                    timeToSleep = TimeSpan.FromMilliseconds(100);
                }

                if (numFails > 0)
                {
                    timeToSleep = retryInterval > timeToSleep ? retryInterval : timeToSleep;
                }

                await Task.Delay(timeToSleep, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                if (await Manage().ConfigureAwait(false))
                {
                    await schedulerSignaler.SignalSchedulingChange(SchedulerConstants.SchedulingSignalDateTime, CancellationToken.None).ConfigureAwait(false);
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }
}