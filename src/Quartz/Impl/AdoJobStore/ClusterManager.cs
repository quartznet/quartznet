using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    internal class ClusterManager
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (ClusterManager));
        private readonly JobStoreSupport jobStoreSupport;

        private QueuedTaskScheduler taskScheduler;
        private readonly CancellationTokenSource cancellationTokenSource;
        private Task<Task> task;

        private int numFails;

        internal ClusterManager(JobStoreSupport jobStoreSupport)
        {
            this.jobStoreSupport = jobStoreSupport;
            cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task InitializeAsync()
        {
            await ManageAsync().ConfigureAwait(false);
            string threadName = $"QuartzScheduler_{jobStoreSupport.InstanceName}-{jobStoreSupport.InstanceId}_ClusterManager";

            taskScheduler = new QueuedTaskScheduler(threadCount: 1, threadPriority: ThreadPriority.AboveNormal, threadName: threadName, useForegroundThreads: !jobStoreSupport.MakeThreadsDaemons);
            task = Task.Factory.StartNew(() => RunAsync(cancellationTokenSource.Token), cancellationTokenSource.Token, TaskCreationOptions.HideScheduler, taskScheduler);
        }

        public async Task ShutdownAsync()
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

        private async Task<bool> ManageAsync()
        {
            bool res = false;
            try
            {
                res = await jobStoreSupport.DoCheckin().ConfigureAwait(false);

                numFails = 0;
                log.Debug("Check-in complete.");
            }
            catch (Exception e)
            {
                if (numFails%jobStoreSupport.RetryableActionErrorLogThreshold == 0)
                {
                    log.ErrorException("Error managing cluster: " + e.Message, e);
                }
                numFails++;
            }
            return res;
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                TimeSpan timeToSleep = jobStoreSupport.ClusterCheckinInterval;
                TimeSpan transpiredTime = SystemTime.UtcNow() - jobStoreSupport.LastCheckin;
                timeToSleep = timeToSleep - transpiredTime;
                if (timeToSleep <= TimeSpan.Zero)
                {
                    timeToSleep = TimeSpan.FromMilliseconds(100);
                }

                if (numFails > 0)
                {
                    timeToSleep = jobStoreSupport.DbRetryInterval > timeToSleep ? jobStoreSupport.DbRetryInterval : timeToSleep;
                }

                await Task.Delay(timeToSleep, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                if (await ManageAsync().ConfigureAwait(false))
                {
                    jobStoreSupport.SignalSchedulingChangeImmediately(SchedulerConstants.SchedulingSignalDateTime);
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }
}