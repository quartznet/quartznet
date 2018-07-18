using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl
{
    /// <summary>
    /// Base class that tries to be persistence technology agnostic and offers some common boiler plate
    /// code needed to hook job store properly.
    /// </summary>
    public abstract class PersistentJobStore<TUnitOfWorkConnection> : IJobStore where TUnitOfWorkConnection : UnitOfWorkConnection
    {
        private static long firedTriggerCounter = SystemTime.UtcNow().Ticks;

        private ClusterManager clusterManager;
        private MisfireHandler misfireHandler;

        private TimeSpan misfireThreshold = TimeSpan.FromMinutes(1); // one minute
        private TimeSpan? misfireHandlerFrequency;
        private string instanceName;

        protected PersistentJobStore()
        {
            Log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        internal ILog Log { get; }

        protected ITypeLoadHelper TypeLoadHelper { get; set; }
        protected ISchedulerSignaler SchedulerSignaler { get; set; }

        protected virtual string AllGroupsPaused => "_$_ALL_GROUPS_PAUSED_$_";

        /// <summary>
        /// Whether or not to obtain locks when inserting new jobs/triggers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to <see langword="true" />, which is safest - some db's (such as
        /// MS SQLServer) seem to require this to avoid deadlocks under high load,
        /// while others seem to do fine without.  Settings this to false means
        /// isolation guarantees between job scheduling and trigger acquisition are
        /// entirely enforced by the database.  Depending on the database and it's
        /// configuration this may cause unusual scheduling behaviors.
        /// </para>
        /// <para>
        /// Setting this property to <see langword="false" /> will provide a
        /// significant performance increase during the addition of new jobs
        /// and triggers.
        /// </para>
        /// </remarks>
        public bool LockOnInsert { get; set; } = true;

        /// <summary>
        /// Whether or not the query and update to acquire a Trigger for firing
        /// should be performed after obtaining an explicit DB lock (to avoid
        /// possible race conditions on the trigger's db row).  This is
        /// is considered unnecessary for most databases (due to the nature of
        ///  the SQL update that is performed), and therefore a superfluous performance hit.
        /// </summary>
        /// <remarks>
        /// However, if batch acquisition is used, it is important for this behavior
        /// to be used for all dbs.
        /// </remarks>
        public bool AcquireTriggersWithinLock { get; set; }

        protected bool SchedulerRunning { get; private set; }
        protected bool IsShutdown { get; private set; }

        /// <summary>
        /// Gets or sets the number of retries before an error is logged for recovery operations.
        /// </summary>
        public int RetryableActionErrorLogThreshold { get; set; } = 4;

        /// <inheritdoc />
        public string InstanceId { get; set; }

        /// <inheritdoc />
        public string InstanceName
        {
            get => instanceName;
            set
            {
                ValidateInstanceName(value);
                instanceName = value;
            }
        }

        protected virtual void ValidateInstanceName(string value)
        {
        }

        /// <inheritdoc />
        public virtual int ThreadPoolSize
        {
            set { }
        }

        /// <summary>
        /// Get whether the threads spawned by this JobStore should be
        /// marked as daemon. Possible threads include the <see cref="MisfireHandler" />
        /// and the <see cref="ClusterManager"/>.
        /// </summary>
        /// <returns></returns>
        public bool MakeThreadsDaemons { get; set; }

        /// <inheritdoc />
        public bool SupportsPersistence => true;

        /// <inheritdoc />
        public abstract long EstimatedTimeToReleaseAndAcquireTrigger { get; }

        /// <inheritdoc />
        public bool Clustered { get; set; }

        /// <summary>
        /// Get or set the frequency at which this instance "checks-in"
        /// with the other instances of the cluster. -- Affects the rate of
        /// detecting failed instances.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan ClusterCheckinInterval { get; set; } = TimeSpan.FromMilliseconds(7500);

        /// <summary>
        /// The time span by which a check-in must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// other scheduler instances in a cluster can consider a "misfired" scheduler
        /// instance as failed or dead.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan ClusterCheckinMisfireThreshold { get; set; } = TimeSpan.FromMilliseconds(7500);

        /// <summary>
        /// Gets or sets the retry interval.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// The time span by which a trigger must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// have its misfire instruction applied.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan MisfireThreshold
        {
            get => misfireThreshold;
            set
            {
                if (value.TotalMilliseconds < 1)
                {
                    throw new ArgumentException("MisfireThreshold must be larger than 0");
                }
                misfireThreshold = value;
            }
        }

        /// <summary>
        /// How often should the misfire handler check for misfires. Defaults to
        /// <see cref="MisfireThreshold"/>.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan MisfireHandlerFrequency
        {
            get => misfireHandlerFrequency.GetValueOrDefault(MisfireThreshold);
            // ReSharper disable once UnusedMember.Global
            set
            {
                if (value.TotalMilliseconds < 1)
                {
                    throw new ArgumentException("MisfireHandlerFrequency must be larger than 0");
                }
                misfireHandlerFrequency = value;
            }
        }

        /// <summary>
        /// Get whether to check to see if there are Triggers that have misfired
        /// before actually acquiring the lock to recover them.  This should be
        /// set to false if the majority of the time, there are misfired
        /// Triggers.
        /// </summary>
        /// <returns></returns>
        public bool DoubleCheckLockMisfireHandler { get; set; } = true;

        /// <summary>
        /// Get or set the maximum number of misfired triggers that the misfire handling
        /// thread will try to recover at one time (within one transaction).  The
        /// default is 20.
        /// </summary>
        public int MaxMisfiresToHandleAtATime { get; set; } = 20;

        protected virtual DateTimeOffset MisfireTime
        {
            get
            {
                DateTimeOffset misfireTime = SystemTime.UtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1*MisfireThreshold.TotalMilliseconds);
                }

                return misfireTime;
            }
        }

        protected abstract IClusterManagementOperations ClusterManagementOperations { get; }

        protected abstract IMisfireHandlerOperations MisfireHandlerOperations { get; }

        /// <inheritdoc />
        public virtual Task Initialize(
            ITypeLoadHelper typeLoadHelper,
            ISchedulerSignaler schedulerSignaler,
            CancellationToken cancellationToken = default)
        {
            TypeLoadHelper = typeLoadHelper;
            SchedulerSignaler = schedulerSignaler;

            return TaskUtil.CompletedTask;
        }

        /// <inheritdoc />
        public virtual async Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            if (Clustered)
            {
                clusterManager = new ClusterManager(
                    InstanceId,
                    InstanceName,
                    MakeThreadsDaemons,
                    RetryInterval,
                    RetryableActionErrorLogThreshold,
                    ClusterCheckinInterval,
                    SchedulerSignaler,
                    ClusterManagementOperations);

                await clusterManager.Initialize().ConfigureAwait(false);
            }
            else
            {
                try
                {
                    await RecoverJobs(cancellationToken).ConfigureAwait(false);
                }
                catch (SchedulerException se)
                {
                    Log.ErrorException("Failure occurred during job recovery: " + se.Message, se);
                    throw new SchedulerConfigException("Failure occurred during job recovery.", se);
                }
            }

            misfireHandler = new MisfireHandler(
                InstanceId,
                InstanceName,
                MakeThreadsDaemons,
                RetryInterval,
                RetryableActionErrorLogThreshold,
                SchedulerSignaler,
                MisfireHandlerOperations);

            misfireHandler.Initialize();

            SchedulerRunning = true;
        }

        /// <inheritdoc />
        public virtual Task SchedulerPaused(CancellationToken cancellationToken = default)
        {
            SchedulerRunning = false;
            return TaskUtil.CompletedTask;
        }

        /// <inheritdoc />
        public virtual Task SchedulerResumed(CancellationToken cancellationToken = default)
        {
            SchedulerRunning = true;
            return TaskUtil.CompletedTask;
        }

        /// <inheritdoc />
        public virtual async Task Shutdown(CancellationToken cancellationToken = default)
        {
            IsShutdown = true;

            if (misfireHandler != null)
            {
                await misfireHandler.Shutdown().ConfigureAwait(false);
            }

            if (clusterManager != null)
            {
                await clusterManager.Shutdown().ConfigureAwait(false);
            }
        }

        private string GetFiredTriggerRecordId()
        {
            Interlocked.Increment(ref firedTriggerCounter);
            return InstanceId + firedTriggerCounter;
        }

        /// <summary>
        /// Will recover any failed or misfired jobs and clean up the data store as appropriate.
        /// </summary>
        private Task RecoverJobs(CancellationToken cancellationToken)
            => ExecuteInNonManagedTXLock(
                LockType.TriggerAccess,
                conn => RecoverJobs(conn, cancellationToken),
                cancellationToken);

        private async Task RecoverJobs(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // update inconsistent job states
                int rows = await UpdateTriggerStatesFromOtherStates(
                    conn,
                    newState: InternalTriggerState.Waiting,
                    oldStates: new[] { InternalTriggerState.Acquired, InternalTriggerState.Blocked },
                    cancellationToken).ConfigureAwait(false);

                rows += await UpdateTriggerStatesFromOtherStates(
                    conn,
                    newState: InternalTriggerState.Paused,
                    oldStates: new[] { InternalTriggerState.PausedAndBlocked },
                    cancellationToken).ConfigureAwait(false);

                Log.Info($"Freed {rows} triggers from 'acquired' / 'blocked' state.");

                // clean up misfired jobs
                await RecoverMisfiredJobs(conn, true, cancellationToken).ConfigureAwait(false);

                // recover jobs marked for recovery that were not fully executed
                var recoveringJobTriggers = await GetTriggersForRecoveringJobs(conn, cancellationToken).ConfigureAwait(false);
                Log.Info($"Recovering {recoveringJobTriggers.Count} jobs that were in-progress at the time of the last shut-down.");

                foreach (IOperableTrigger trigger in recoveringJobTriggers)
                {
                    if (await JobExists(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false))
                    {
                        trigger.ComputeFirstFireTimeUtc(null);
                        await StoreTrigger(conn, trigger, null, false, InternalTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                    }
                }
                Log.Info("Recovery complete.");

                // remove lingering 'complete' triggers...
                var triggersInState = await GetTriggersInState(conn, InternalTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                foreach (var trigger in triggersInState)
                {
                    await RemoveTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
                }
                Log.Info($"Removed {triggersInState.Count} 'complete' triggers.");

                // clean up any fired trigger entries
                int n = await DeleteFiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                Log.Info("Removed " + n + " stale fired job entries.");
            }
            catch (JobPersistenceException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't recover jobs: " + e.Message, e);
            }
        }

        protected abstract Task<int> DeleteFiredTriggers(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        protected abstract Task<IReadOnlyCollection<TriggerKey>> GetTriggersInState(TUnitOfWorkConnection conn,
            InternalTriggerState state,
            CancellationToken cancellationToken);

        protected abstract Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForRecoveringJobs(TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        protected abstract Task<int> UpdateTriggerStatesFromOtherStates(
            TUnitOfWorkConnection conn,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken);

        public virtual async Task<RecoverMisfiredJobsResult> RecoverMisfiredJobs(
            TUnitOfWorkConnection conn,
            bool recovering,
            CancellationToken cancellationToken = default)
        {
            // If recovering, we want to handle all of the misfired
            // triggers right away.
            int maxMisfiresToHandleAtATime = recovering ? -1 : MaxMisfiresToHandleAtATime;

            List<TriggerKey> misfiredTriggers = new List<TriggerKey>();
            DateTimeOffset earliestNewTime = DateTimeOffset.MaxValue;

            // We must still look for the MISFIRED state in case triggers were left
            // in this state when upgrading to this version that does not support it.
            bool hasMoreMisfiredTriggers = await GetMisfiredTriggersInWaitingState(
                conn,
                maxMisfiresToHandleAtATime,
                misfiredTriggers,
                cancellationToken).ConfigureAwait(false);

            if (hasMoreMisfiredTriggers)
            {
                Log.Info($"Handling the first {misfiredTriggers.Count} triggers that missed their scheduled fire-time.  More misfired triggers remain to be processed.");
            }
            else if (misfiredTriggers.Count > 0)
            {
                Log.Info($"Handling {misfiredTriggers.Count} trigger(s) that missed their scheduled fire-time.");
            }
            else
            {
                Log.Debug("Found 0 triggers that missed their scheduled fire-time.");
                return RecoverMisfiredJobsResult.NoOp;
            }

            foreach (TriggerKey triggerKey in misfiredTriggers)
            {
                IOperableTrigger trigger = await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                if (trigger == null)
                {
                    continue;
                }

                await DoUpdateOfMisfiredTrigger(
                    conn,
                    trigger,
                    forceState: false,
                    InternalTriggerState.Waiting,
                    recovering,
                    cancellationToken).ConfigureAwait(false);

                DateTimeOffset? nextTime = trigger.GetNextFireTimeUtc();
                if (nextTime.HasValue && nextTime.Value < earliestNewTime)
                {
                    earliestNewTime = nextTime.Value;
                }
            }

            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }

        protected abstract Task<bool> GetMisfiredTriggersInWaitingState(TUnitOfWorkConnection conn,
            int count,
            List<TriggerKey> resultList,
            CancellationToken cancellationToken);

        protected virtual async Task<bool> UpdateMisfiredTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState newStateIfNotComplete,
            bool forceState,
            CancellationToken cancellationToken)
        {
            try
            {
                IOperableTrigger trigger = await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                DateTimeOffset misfireTime = SystemTime.UtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1*MisfireThreshold.TotalMilliseconds);
                }

                if (trigger.GetNextFireTimeUtc().GetValueOrDefault() > misfireTime)
                {
                    return false;
                }

                await DoUpdateOfMisfiredTrigger(
                    conn,
                    trigger,
                    forceState,
                    newStateIfNotComplete,
                    recovering: false,
                    cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't update misfired trigger '{triggerKey}': {e.Message}", e);
            }
        }

        private async Task DoUpdateOfMisfiredTrigger(
            TUnitOfWorkConnection conn,
            IOperableTrigger trig,
            bool forceState,
            InternalTriggerState newStateIfNotComplete,
            bool recovering,
            CancellationToken cancellationToken)
        {
            ICalendar cal = null;
            if (trig.CalendarName != null)
            {
                cal = await RetrieveCalendar(conn, trig.CalendarName, cancellationToken).ConfigureAwait(false);
            }

            await SchedulerSignaler.NotifyTriggerListenersMisfired(trig, cancellationToken).ConfigureAwait(false);

            trig.UpdateAfterMisfire(cal);

            if (!trig.GetNextFireTimeUtc().HasValue)
            {
                await StoreTrigger(conn, trig, null, true, InternalTriggerState.Complete, forceState, recovering, cancellationToken).ConfigureAwait(false);
                await SchedulerSignaler.NotifySchedulerListenersFinalized(trig, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StoreTrigger(conn, trig, null, true, newStateIfNotComplete, forceState, recovering, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task StoreJobAndTrigger(
            IJobDetail newJob,
            IOperableTrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            await ExecuteInLock(LockOnInsert ? LockType.TriggerAccess : LockType.None, async conn =>
            {
                await StoreJob(conn, newJob, false, cancellationToken).ConfigureAwait(false);
                await StoreTrigger(conn, newTrigger, newJob, false, InternalTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task StoreJob(
            IJobDetail newJob,
            bool replaceExisting,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockOnInsert || replaceExisting ? LockType.TriggerAccess : LockType.None,
                conn => StoreJob(conn, newJob, replaceExisting, cancellationToken),
                cancellationToken);
        }

        protected async Task StoreJob(
            TUnitOfWorkConnection conn,
            IJobDetail jobDetail,
            bool replaceExisting,
            CancellationToken cancellationToken)
        {
            var existingJob = await JobExists(conn, jobDetail.Key, cancellationToken).ConfigureAwait(false);
            if (existingJob)
            {
                if (!replaceExisting)
                {
                    throw new ObjectAlreadyExistsException(jobDetail);
                }
            }
            await DoStoreJob(conn, jobDetail, existingJob, cancellationToken).ConfigureAwait(false);
        }

        protected abstract Task DoStoreJob(
            TUnitOfWorkConnection conn,
            IJobDetail jobDetail,
            bool existingJob,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task StoreJobsAndTriggers(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            await ExecuteInLock(
                LockOnInsert || replace ? LockType.TriggerAccess : LockType.None, async conn =>
                {
                    // TODO: make this more efficient with a true bulk operation...
                    foreach (var pair in triggersAndJobs)
                    {
                        var job = pair.Key;
                        var triggers = pair.Value;
                        await StoreJob(conn, job, replace, cancellationToken).ConfigureAwait(false);
                        foreach (var trigger in triggers)
                        {
                            await StoreTrigger(
                                conn,
                                (IOperableTrigger) trigger,
                                job,
                                replace,
                                InternalTriggerState.Waiting,
                                forceState: false,
                                false,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task TriggeredJobComplete(
            IOperableTrigger trigger,
            IJobDetail jobDetail,
            SchedulerInstruction triggerInstCode,
            CancellationToken cancellationToken = default)
        {
            return RetryExecuteInNonManagedTXLock(
                LockType.TriggerAccess, async conn =>
                {
                    try
                    {
                        if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
                        {
                            if (!trigger.GetNextFireTimeUtc().HasValue)
                            {
                                // double check for possible reschedule within job
                                // execution, which would cancel the need to delete...
                                TriggerStatus stat = await GetTriggerStatus(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                                if (stat != null && !stat.NextFireTimeUtc.HasValue)
                                {
                                    await RemoveTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                await RemoveTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                                conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                            }
                        }
                        else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
                        {
                            await UpdateTriggerState(conn, trigger.Key, InternalTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                        }
                        else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
                        {
                            Log.Info("Trigger " + trigger.Key + " set to ERROR state.");
                            await UpdateTriggerState(conn, trigger.Key, InternalTriggerState.Error, cancellationToken).ConfigureAwait(false);
                            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                        }
                        else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
                        {
                            await UpdateTriggerStatesForJob(conn, trigger.JobKey, InternalTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                        }
                        else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
                        {
                            Log.Info("All triggers of Job " + trigger.JobKey + " set to ERROR state.");
                            await UpdateTriggerStatesForJob(conn, trigger.JobKey, InternalTriggerState.Error, cancellationToken).ConfigureAwait(false);
                            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                        }

                        if (jobDetail.ConcurrentExecutionDisallowed)
                        {
                            await UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, InternalTriggerState.Waiting, InternalTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                            await UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, InternalTriggerState.Paused, InternalTriggerState.PausedAndBlocked, cancellationToken).ConfigureAwait(false);
                            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                        }

                        if (jobDetail.PersistJobDataAfterExecution)
                        {
                            try
                            {
                                if (jobDetail.JobDataMap.Dirty)
                                {
                                    await UpdateJobData(conn, jobDetail, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            catch (IOException e)
                            {
                                throw new JobPersistenceException("Couldn't serialize job data: " + e.Message, e);
                            }
                            catch (Exception e)
                            {
                                throw new JobPersistenceException("Couldn't update job data: " + e.Message, e);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("Couldn't update trigger state(s): " + e.Message, e);
                    }

                    try
                    {
                        await DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("Couldn't delete fired trigger: " + e.Message, e);
                    }
                },
                cancellationToken);
        }

        protected abstract Task UpdateTriggerStatesForJobFromOtherState(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            CancellationToken cancellationToken);

        protected abstract Task UpdateJobData(
            TUnitOfWorkConnection conn,
            IJobDetail jobDetail,
            CancellationToken cancellationToken);

        protected abstract Task DeleteFiredTrigger(
            TUnitOfWorkConnection conn,
            string triggerFireInstanceId,
            CancellationToken cancellationToken);

        protected abstract Task UpdateTriggerStatesForJob(
            TUnitOfWorkConnection conn,
            JobKey triggerJobKey,
            InternalTriggerState state,
            CancellationToken cancellationToken);

        protected abstract Task UpdateTriggerState(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState state,
            CancellationToken cancellationToken);


        protected async Task RetryExecuteInNonManagedTXLock(
            LockType lockType,
            Func<TUnitOfWorkConnection, Task> txCallback,
            CancellationToken cancellationToken)
        {
            await RetryExecuteInNonManagedTXLock<object>(lockType, async holder =>
            {
                await txCallback(holder).ConfigureAwait(false);
                return null;
            }, cancellationToken).ConfigureAwait(false);
        }

        protected virtual async Task<T> RetryExecuteInNonManagedTXLock<T>(
            LockType lockType,
            Func<TUnitOfWorkConnection, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            for (int retry = 1; !IsShutdown; retry++)
            {
                try
                {
                    return await ExecuteInNonManagedTXLock(lockType, txCallback, null, cancellationToken).ConfigureAwait(false);
                }
                catch (JobPersistenceException jpe)
                {
                    if (retry%RetryableActionErrorLogThreshold == 0)
                    {
                        await SchedulerSignaler.NotifySchedulerListenersError("An error occurred while " + txCallback, jpe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Log.ErrorException("retryExecuteInNonManagedTXLock: RuntimeException " + e.Message, e);
                }

                // retry every N seconds (the db connection must be failed)
                await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("JobStore is shutdown - aborting retry");
        }

        protected async Task ExecuteInNonManagedTXLock(
            LockType lockType,
            Func<TUnitOfWorkConnection, Task> txCallback,
            CancellationToken cancellationToken)
        {
            await ExecuteInNonManagedTXLock(lockType, async conn =>
            {
                await txCallback(conn).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        protected Task<T> ExecuteInNonManagedTXLock<T>(
            LockType lockType,
            Func<TUnitOfWorkConnection, Task<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInNonManagedTXLock(lockType, txCallback, null, cancellationToken);
        }

        /// <summary>
        /// Execute the given callback having optionally acquired the given lock.
        /// This uses the non-managed transaction connection.
        /// </summary>
        /// <param name="lockType">
        /// The name of the lock to acquire, for example
        /// "TRIGGER_ACCESS".  If null, then no lock is acquired, but the
        /// lockCallback is still executed in a non-managed transaction.
        /// </param>
        /// <param name="txCallback">
        /// The callback to execute after having acquired the given lock.
        /// </param>
        /// <param name="txValidator"></param>>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        protected abstract Task<T> ExecuteInNonManagedTXLock<T>(
            LockType lockType,
            Func<TUnitOfWorkConnection, Task<T>> txCallback,
            Func<TUnitOfWorkConnection, T, Task<bool>> txValidator,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<bool> RemoveCalendar(
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteInLock(
                    LockType.TriggerAccess,
                    conn => RemoveCalendar(conn, calendarName, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't remove calendar: {e.Message}", e);
            }
        }

        protected abstract Task<bool> RemoveCalendar(
            TUnitOfWorkConnection conn,
            string calendarName,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<ICalendar> RetrieveCalendar(
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => RetrieveCalendar(conn, calendarName, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                throw new JobPersistenceException($"Couldn't retrieve calendar because it couldn't be deserialized: {e.Message}", e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't retrieve calendar: {e.Message}", e);
            }
        }

        protected abstract Task<ICalendar> RetrieveCalendar(
            TUnitOfWorkConnection conn,
            string calendarName,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<int> GetNumberOfJobs(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetNumberOfJobs(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of jobs: " + e.Message, e);
            }
        }

        protected abstract Task<int> GetNumberOfJobs(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<int> GetNumberOfTriggers(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetNumberOfTriggers(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of triggers: " + e.Message, e);
            }
        }

        protected abstract Task<int> GetNumberOfTriggers(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<int> GetNumberOfCalendars(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetNumberOfCalendars(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain number of calendars: " + e.Message, e);
            }
        }

        protected abstract Task<int> GetNumberOfCalendars(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetJobKeys(conn, matcher, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't obtain job names: " + e.Message, e);
            }
        }

        protected abstract Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            TUnitOfWorkConnection conn,
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken);

        public async Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetTriggerKeys(conn, matcher, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't obtain trigger names: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            TUnitOfWorkConnection conn,
            GroupMatcher<TriggerKey> groupMatcher,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetJobGroupNames(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't obtain job groups: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<string>> GetJobGroupNames(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetTriggerGroupNames(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't obtain trigger groups: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetCalendarNames(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't obtain calendar names: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<string>> GetCalendarNames(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetTriggersForJob(conn, jobKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't obtain triggers for job: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<TriggerState> GetTriggerState(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetTriggerState(conn, triggerKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't determine state of trigger ({triggerKey}): {e.Message}", e);
            }
        }

        protected abstract Task<TriggerState> GetTriggerState(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        protected abstract Task<InternalTriggerState> GetInternalTriggerState(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        public async Task PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteInLock(
                    LockType.TriggerAccess,
                    conn => PauseTrigger(conn, triggerKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't pause trigger '{triggerKey}': {e.Message}", e);
            }
        }

        private async Task PauseTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var oldState = await GetInternalTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (oldState == InternalTriggerState.Waiting || oldState== InternalTriggerState.Acquired)
            {
                await UpdateTriggerState(conn, triggerKey, InternalTriggerState.Paused, cancellationToken).ConfigureAwait(false);
            }
            else if (oldState == InternalTriggerState.Blocked)
            {
                await UpdateTriggerState(conn, triggerKey, InternalTriggerState.PausedAndBlocked, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<IReadOnlyCollection<string>> PauseTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess,
                conn => PauseTriggerGroup(conn, matcher, cancellationToken),
                cancellationToken);
        }

        protected abstract Task<IReadOnlyCollection<string>> PauseTriggerGroup(
            TUnitOfWorkConnection conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public Task PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(LockType.TriggerAccess, async conn =>
            {
                var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                foreach (IOperableTrigger trigger in triggers)
                {
                    await PauseTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyCollection<string>> PauseJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock<IReadOnlyCollection<string>>(LockType.TriggerAccess, async conn =>
            {
                var groupNames = new ReadOnlyCompatibleHashSet<string>();
                var jobNames = await GetJobKeys(conn, matcher, cancellationToken).ConfigureAwait(false);

                foreach (JobKey jobKey in jobNames)
                {
                    var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        await PauseTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    }
                    groupNames.Add(jobKey.Group);
                }

                return groupNames;
            }, cancellationToken);
        }

        public async Task ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteInLock(
                    LockType.TriggerAccess,
                    conn => ResumeTrigger(conn, triggerKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't resume trigger '{triggerKey}': {e.Message}", e);
            }
        }

        protected async Task ResumeTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            TriggerStatus status = await GetTriggerStatus(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            if (status?.NextFireTimeUtc == null || status.NextFireTimeUtc == DateTimeOffset.MinValue)
            {
                return;
            }

            bool blocked = status.Status == InternalTriggerState.PausedAndBlocked;

            var newState = await CheckBlockedState(conn, status.JobKey, InternalTriggerState.Waiting, cancellationToken).ConfigureAwait(false);

            bool misfired = false;

            if (SchedulerRunning && status.NextFireTimeUtc.Value < SystemTime.UtcNow())
            {
                misfired = await UpdateMisfiredTrigger(conn, triggerKey, newState, true, cancellationToken).ConfigureAwait(false);
            }

            if (!misfired)
            {
                var oldStates = blocked
                    ? new[] {InternalTriggerState.PausedAndBlocked}
                    : new[] {InternalTriggerState.Paused};

                await UpdateTriggerStateFromOtherStates(conn, triggerKey, newState, oldStates, cancellationToken).ConfigureAwait(false);
            }
        }

        protected abstract Task<TriggerStatus> GetTriggerStatus(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        protected abstract Task<int> UpdateTriggerStateFromOtherStates(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken);

        protected abstract Task<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            DateTimeOffset nextFireTime,
            CancellationToken cancellationToken);

        public async Task<IReadOnlyCollection<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteInLock(
                    LockType.TriggerAccess,
                    conn => ResumeTriggers(conn, matcher, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't resume trigger group '{matcher}': {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<string>> ResumeTriggers(
            TUnitOfWorkConnection conn,
            GroupMatcher<TriggerKey> groupMatcher,
            CancellationToken cancellationToken);

        public async Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => GetPausedTriggerGroups(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't determine paused trigger groups: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public Task ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(LockType.TriggerAccess, async conn =>
            {
                var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                foreach (IOperableTrigger trigger in triggers)
                {
                    await ResumeTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyCollection<string>> ResumeJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock<IReadOnlyCollection<string>>(
                LockType.TriggerAccess,
                async conn =>
                {
                    IReadOnlyCollection<JobKey> jobKeys = await GetJobKeys(conn, matcher, cancellationToken).ConfigureAwait(false);
                    var groupNames = new ReadOnlyCompatibleHashSet<string>();

                    foreach (JobKey jobKey in jobKeys)
                    {
                        var triggers = await GetTriggersForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                        foreach (IOperableTrigger trigger in triggers)
                        {
                            await ResumeTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                        }

                        groupNames.Add(jobKey.Group);
                    }
                    return groupNames;
                }, cancellationToken);
        }

        public Task PauseAll(CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess,
                async conn =>
                {
                    try
                    {
                        var groupNames = await GetTriggerGroupNames(conn, cancellationToken).ConfigureAwait(false);

                        foreach (string groupName in groupNames)
                        {
                            await PauseTriggerGroup(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false);
                        }

                        try
                        {
                            if (!await IsTriggerGroupPaused(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false))
                            {
                                await AddPausedTriggerGroup(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            throw new JobPersistenceException("Couldn't pause all trigger groups: " + e.Message, e);
                        }
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException($"Couldn't pause all trigger groups: {e.Message}", e);
                    }
                },
                cancellationToken);
        }

        /// <inheritdoc />
        public Task ResumeAll(CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess, async conn =>
                {
                    try
                    {
                        var triggerGroupNames = await GetTriggerGroupNames(conn, cancellationToken).ConfigureAwait(false);

                        foreach (string groupName in triggerGroupNames)
                        {
                            await ResumeTriggers(conn, GroupMatcher<TriggerKey>.GroupEquals(groupName), cancellationToken).ConfigureAwait(false);
                        }

                        await ClearAllTriggerGroupsPausedFlag(conn, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("Couldn't resume all trigger groups: " + e.Message, e);
                    }
                }, cancellationToken);
        }

        protected abstract Task ClearAllTriggerGroupsPausedFlag(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(
            DateTimeOffset noLaterThan,
            int maxCount,
            TimeSpan timeWindow,
            CancellationToken cancellationToken = default)
        {
            LockType lockName;
            if (AcquireTriggersWithinLock || maxCount > 1)
            {
                lockName = LockType.TriggerAccess;
            }
            else
            {
                lockName = LockType.None;
            }

            return await ExecuteInNonManagedTXLock(
                lockName,
                conn => AcquireNextTrigger(conn, noLaterThan, maxCount, timeWindow, cancellationToken),
                async (conn, result) =>
                {
                    try
                    {
                        var acquired = await GetInstancesFiredTriggerRecords(conn, InstanceId, cancellationToken).ConfigureAwait(false);
                        var fireInstanceIds = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            fireInstanceIds.Add(ft.FireInstanceId);
                        }
                        foreach (IOperableTrigger tr in result)
                        {
                            if (fireInstanceIds.Contains(tr.FireInstanceId))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("error validating trigger acquisition", e);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        protected virtual async Task<IReadOnlyList<IOperableTrigger>> AcquireNextTrigger(
            TUnitOfWorkConnection conn,
            DateTimeOffset noLaterThan,
            int maxCount,
            TimeSpan timeWindow,
            CancellationToken cancellationToken = default)
        {
            if (timeWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeWindow));
            }

            List<IOperableTrigger> acquiredTriggers = new List<IOperableTrigger>();
            HashSet<JobKey> acquiredJobKeysForNoConcurrentExec = new HashSet<JobKey>();
            const int MaxDoLoopRetry = 3;
            int currentLoopCount = 0;

            do
            {
                currentLoopCount++;
                try
                {
                    var keys = await GetTriggerToAcquire(
                        conn,
                        noLaterThan + timeWindow,
                        MisfireTime,
                        maxCount,
                        cancellationToken).ConfigureAwait(false);

                    // No trigger is ready to fire yet.
                    if (keys == null || keys.Count == 0)
                    {
                        return acquiredTriggers;
                    }

                    DateTimeOffset batchEnd = noLaterThan;

                    foreach (TriggerKey triggerKey in keys)
                    {
                        // If our trigger is no longer available, try a new one.
                        IOperableTrigger nextTrigger = await RetrieveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                        if (nextTrigger == null)
                        {
                            continue; // next trigger
                        }

                        // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                        // put it back into the timeTriggers set and continue to search for next trigger.
                        JobKey jobKey = nextTrigger.JobKey;
                        IJobDetail job;
                        try
                        {
                            job = await RetrieveJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                        }
                        catch (JobPersistenceException jpe)
                        {
                            try
                            {
                                Log.ErrorException("Error retrieving job, setting trigger state to ERROR.", jpe);
                                await UpdateTriggerState(conn, triggerKey, InternalTriggerState.Error, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log.ErrorException("Unable to set trigger state to ERROR.", ex);
                            }
                            continue;
                        }

                        if (job.ConcurrentExecutionDisallowed)
                        {
                            if (acquiredJobKeysForNoConcurrentExec.Contains(jobKey))
                            {
                                continue; // next trigger
                            }
                            acquiredJobKeysForNoConcurrentExec.Add(jobKey);
                        }

                        var nextFireTimeUtc = nextTrigger.GetNextFireTimeUtc();

                        if (nextFireTimeUtc == null || nextFireTimeUtc > batchEnd)
                        {
                            break;
                        }

                        // We now have a acquired trigger, let's add to return list.
                        // If our trigger was no longer in the expected state, try a new one.
                        int rowsUpdated = await UpdateTriggerStateFromOtherStateWithNextFireTime(
                            conn,
                            triggerKey,
                            newState: InternalTriggerState.Acquired,
                            oldState:InternalTriggerState.Waiting,
                            nextFireTimeUtc.Value,
                            cancellationToken).ConfigureAwait(false);

                        if (rowsUpdated <= 0)
                        {
                            // TODO: Hum... shouldn't we log a warning here?
                            continue; // next trigger
                        }
                        nextTrigger.FireInstanceId = GetFiredTriggerRecordId();
                        await AddFiredTrigger(
                            conn,
                            nextTrigger,
                            state: InternalTriggerState.Acquired,
                            jobDetail: null,
                            cancellationToken).ConfigureAwait(false);

                        if (acquiredTriggers.Count == 0)
                        {
                            var now = SystemTime.UtcNow();
                            var nextFireTime = nextTrigger.GetNextFireTimeUtc().GetValueOrDefault(DateTimeOffset.MinValue);
                            var max = now > nextFireTime ? now : nextFireTime;

                            batchEnd = max + timeWindow;
                        }

                        acquiredTriggers.Add(nextTrigger);
                    }

                    // if we didn't end up with any trigger to fire from that first
                    // batch, try again for another batch. We allow with a max retry count.
                    if (acquiredTriggers.Count == 0 && currentLoopCount < MaxDoLoopRetry)
                    {
                        continue;
                    }

                    // We are done with the while loop.
                    break;
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't acquire next trigger: " + e.Message, e);
                }
            } while (true);

            // Return the acquired trigger list
            return acquiredTriggers;
        }

        /// <summary>
        /// Select the next trigger which will fire to fire between the two given timestamps
        /// in ascending order of fire time, and then descending by priority.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="noLaterThan">highest value of <see cref="ITrigger.GetNextFireTimeUtc" /> of the triggers (exclusive)</param>
        /// <param name="noEarlierThan">highest value of <see cref="ITrigger.GetNextFireTimeUtc" /> of the triggers (inclusive)</param>
        /// <param name="maxCount">maximum number of trigger keys allow to acquired in the returning list.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>A (never null, possibly empty) list of the identifiers (Key objects) of the next triggers to be fired.</returns>
        protected abstract Task<IReadOnlyCollection<TriggerKey>> GetTriggerToAcquire(
            TUnitOfWorkConnection conn,
            DateTimeOffset noLaterThan,
            DateTimeOffset noEarlierThan,
            int maxCount,
            CancellationToken cancellationToken);

        protected abstract Task AddFiredTrigger(
            TUnitOfWorkConnection conn,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail jobDetail,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public Task ReleaseAcquiredTrigger(
            IOperableTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return RetryExecuteInNonManagedTXLock(
                LockType.TriggerAccess, async conn =>
                {
                    try
                    {
                        await UpdateTriggerStateFromOtherStates(
                            conn,
                            trigger.Key,
                            InternalTriggerState.Waiting,
                            new [] { InternalTriggerState.Acquired },
                            cancellationToken).ConfigureAwait(false);

                        await DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("Couldn't release acquired trigger: " + e.Message, e);
                    }
                },
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(
            IReadOnlyCollection<IOperableTrigger> triggers,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteInNonManagedTXLock(
                LockType.TriggerAccess,
                async conn =>
                {
                    List<TriggerFiredResult> results = new List<TriggerFiredResult>();

                    foreach (IOperableTrigger trigger in triggers)
                    {
                        TriggerFiredResult result;
                        try
                        {
                            TriggerFiredBundle bundle = await TriggerFired(conn, trigger, cancellationToken).ConfigureAwait(false);
                            result = new TriggerFiredResult(bundle);
                        }
                        catch (JobPersistenceException jpe)
                        {
                            Log.ErrorFormat("Caught job persistence exception: " + jpe.Message, jpe);
                            result = new TriggerFiredResult(jpe);
                        }
                        catch (Exception ex)
                        {
                            Log.ErrorFormat("Caught exception: " + ex.Message, ex);
                            result = new TriggerFiredResult(ex);
                        }
                        results.Add(result);
                    }

                    return results;
                },
                async (conn, result) =>
                {
                    try
                    {
                        var acquired = await GetInstancesFiredTriggerRecords(conn, InstanceId, cancellationToken).ConfigureAwait(false);
                        var executingTriggers = new HashSet<string>();
                        foreach (FiredTriggerRecord ft in acquired)
                        {
                            if (ft.FireInstanceState == InternalTriggerState.Executing)
                            {
                                executingTriggers.Add(ft.FireInstanceId);
                            }
                        }
                        foreach (TriggerFiredResult tr in result)
                        {
                            if (tr.TriggerFiredBundle != null && executingTriggers.Contains(tr.TriggerFiredBundle.Trigger.FireInstanceId))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    catch (Exception e)
                    {
                        throw new JobPersistenceException("error validating trigger acquisition", e);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        protected abstract Task<IReadOnlyCollection<FiredTriggerRecord>> GetInstancesFiredTriggerRecords(
            TUnitOfWorkConnection conn,
            string instanceId,
            CancellationToken cancellationToken);

        private async Task<TriggerFiredBundle> TriggerFired(
            TUnitOfWorkConnection conn,
            IOperableTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            IJobDetail job;
            ICalendar cal = null;

            // Make sure trigger wasn't deleted, paused, or completed...
            try
            {
                // if trigger was deleted, state will be StateDeleted
                var state = await GetInternalTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                if (state != InternalTriggerState.Acquired)
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't select trigger state: " + e.Message, e);
            }

            try
            {
                job = await RetrieveJob(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
                if (job == null)
                {
                    return null;
                }
            }
            catch (JobPersistenceException jpe)
            {
                try
                {
                    Log.ErrorException("Error retrieving job, setting trigger state to ERROR.", jpe);
                    await UpdateTriggerState(conn, trigger.Key, InternalTriggerState.Error, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception sqle)
                {
                    Log.ErrorException("Unable to set trigger state to ERROR.", sqle);
                }
                throw;
            }

            if (trigger.CalendarName != null)
            {
                cal = await RetrieveCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                if (cal == null)
                {
                    return null;
                }
            }

            try
            {
                await UpdateFiredTrigger(conn, trigger, InternalTriggerState.Executing, job, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't update fired trigger: {e.Message}", e);
            }

            DateTimeOffset? prevFireTime = trigger.GetPreviousFireTimeUtc();

            // call triggered - to update the trigger's next-fire-time state...
            trigger.Triggered(cal);

            var state2 = InternalTriggerState.Waiting;
            bool force = true;

            if (job.ConcurrentExecutionDisallowed)
            {
                state2 = InternalTriggerState.Blocked;
                force = false;
                try
                {
                    await UpdateTriggerStatesForJobFromOtherState(conn, job.Key, InternalTriggerState.Blocked, InternalTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
                    await UpdateTriggerStatesForJobFromOtherState(conn, job.Key, InternalTriggerState.Blocked, InternalTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                    await UpdateTriggerStatesForJobFromOtherState(conn, job.Key, InternalTriggerState.PausedAndBlocked, InternalTriggerState.Paused, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't update states of blocked triggers: " + e.Message, e);
                }
            }

            if (!trigger.GetNextFireTimeUtc().HasValue)
            {
                state2 = InternalTriggerState.Complete;
                force = true;
            }

            await StoreTrigger(conn, trigger, job, true, state2, force, false, cancellationToken).ConfigureAwait(false);

            job.JobDataMap.ClearDirtyFlag();

            return new TriggerFiredBundle(
                job,
                trigger,
                cal,
                trigger.Key.Group.Equals(SchedulerConstants.DefaultRecoveryGroup),
                SystemTime.UtcNow(),
                trigger.GetPreviousFireTimeUtc(),
                prevFireTime,
                trigger.GetNextFireTimeUtc());
        }

        protected abstract Task UpdateFiredTrigger(
            TUnitOfWorkConnection conn,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<bool> ReplaceTrigger(
            TriggerKey triggerKey,
            IOperableTrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteInLock(
                    LockType.TriggerAccess,
                    async conn =>
                    {
                        IJobDetail job = await GetJobForTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                        if (job == null)
                        {
                            return false;
                        }

                        if (!newTrigger.JobKey.Equals(job.Key))
                        {
                            throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                        }

                        return await DoReplaceTrigger(conn, triggerKey, newTrigger, job, cancellationToken).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't replace trigger: {e.Message}", e);
            }
        }

        protected virtual async Task<bool> DoReplaceTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            IOperableTrigger newTrigger,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

            await StoreTrigger(
                conn,
                newTrigger,
                job,
                replaceExisting: false,
                InternalTriggerState.Waiting,
                forceState: false,
                recovering: false,
                cancellationToken).ConfigureAwait(false);

            return removedTrigger;
        }

        protected abstract Task<IJobDetail> GetJobForTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        protected abstract Task<bool> DeleteTriggerAndChildren(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        protected abstract Task<bool> DeleteJobAndChildren(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<IOperableTrigger> RetrieveTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => RetrieveTrigger(conn, triggerKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't retrieve trigger: {e.Message}", e);
            }
        }

        /// <inheritdoc />
        public async Task<bool> CalendarExists(
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => CalendarExists(conn, calendarName, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't check for existence of calendar: {e.Message}", e);
            }
        }

        protected abstract Task<bool> CalendarExists(
            TUnitOfWorkConnection conn,
            string calendarName,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<bool> CheckExists(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => JobExists(conn, jobKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't check for existence of job: {e.Message}", e);
            }
        }

        protected abstract Task<bool> JobExists(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<bool> CheckExists(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteWithoutLock(
                    conn => TriggerExists(conn, triggerKey, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't check for existence of trigger: {e.Message}", e);
            }
        }

        protected abstract Task<bool> TriggerExists(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task ClearAllSchedulingData(CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteInLock(
                    LockType.TriggerAccess,
                    conn => ClearAllSchedulingData(conn, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Error clearing scheduling data: {e.Message}", e);
            }
        }

        protected abstract Task ClearAllSchedulingData(
            TUnitOfWorkConnection conn,
            CancellationToken cancellationToken);

        public async Task StoreCalendar(
            string name,
            ICalendar calendar,
            bool replaceExisting,
            bool updateTriggers,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteInLock(
                    LockOnInsert || updateTriggers ? LockType.TriggerAccess : LockType.None,
                    conn => StoreCalendar(conn, name, calendar, replaceExisting, updateTriggers, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                throw new JobPersistenceException(
                    "Couldn't store calendar because the BLOB couldn't be serialized: " + e.Message, e);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't store calendar: " + e.Message, e);
            }
        }

        protected abstract Task StoreCalendar(
            TUnitOfWorkConnection conn,
            string name,
            ICalendar calendar,
            bool replaceExisting,
            bool updateTriggers,
            CancellationToken cancellationToken);

        protected abstract Task<IOperableTrigger> RetrieveTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken);

        public Task StoreTrigger(
            IOperableTrigger newTrigger,
            bool replaceExisting,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess,
                conn => StoreTrigger(conn, newTrigger, null, replaceExisting, InternalTriggerState.Waiting, false, false, cancellationToken),
                cancellationToken);
        }

        protected async Task StoreTrigger(
            TUnitOfWorkConnection conn,
            IOperableTrigger newTrigger,
            IJobDetail job,
            bool replaceExisting,
            InternalTriggerState state,
            bool forceState,
            bool recovering,
            CancellationToken cancellationToken)
        {
            bool existingTrigger = await TriggerExists(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);

            if (existingTrigger && !replaceExisting)
            {
                throw new ObjectAlreadyExistsException(newTrigger);
            }

            try
            {
                if (!forceState)
                {
                    bool shouldBePaused = await IsTriggerGroupPaused(conn, newTrigger.Key.Group, cancellationToken).ConfigureAwait(false);

                    if (!shouldBePaused)
                    {
                        shouldBePaused = await IsTriggerGroupPaused(conn, AllGroupsPaused, cancellationToken).ConfigureAwait(false);

                        if (shouldBePaused)
                        {
                            await AddPausedTriggerGroup(conn, newTrigger.Key.Group, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (shouldBePaused && (state == InternalTriggerState.Waiting || state == InternalTriggerState.Acquired))
                    {
                        state = InternalTriggerState.Paused;
                    }
                }

                if (job == null)
                {
                    job = await RetrieveJob(conn, newTrigger.JobKey, cancellationToken).ConfigureAwait(false);
                }

                if (job == null)
                {
                    throw new JobPersistenceException($"The job ({newTrigger.JobKey}) referenced by the trigger does not exist.");
                }

                if (job.ConcurrentExecutionDisallowed && !recovering)
                {
                    state = await CheckBlockedState(conn, job.Key, state, cancellationToken).ConfigureAwait(false);
                }

                await StoreTrigger(conn, existingTrigger, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                string message = $"Couldn't store trigger '{newTrigger.Key}' for '{newTrigger.JobKey}' job: {e.Message}";
                throw new JobPersistenceException(message, e);
            }
        }

        protected abstract Task AddPausedTriggerGroup(
            TUnitOfWorkConnection conn,
            string groupName,
            CancellationToken cancellationToken);

        protected abstract Task<bool> IsTriggerGroupPaused(
            TUnitOfWorkConnection conn,
            string groupName,
            CancellationToken cancellationToken);

        protected abstract Task StoreTrigger(
            TUnitOfWorkConnection conn,
            bool existingTrigger,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken);

        public async Task<bool> RemoveTriggers(
            IReadOnlyCollection<TriggerKey> triggerKeys,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteInLock(
                LockType.TriggerAccess,
                async conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        allFound = await RemoveTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false) && allFound;
                    }

                    return allFound;
                }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<bool> RemoveTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess,
                conn => RemoveTrigger(conn, triggerKey, null, cancellationToken),
                cancellationToken);
        }

        protected Task<bool> RemoveTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken) => RemoveTrigger(conn, triggerKey, null, cancellationToken);

        protected abstract Task<bool> RemoveTrigger(
            TUnitOfWorkConnection conn,
            TriggerKey triggerKey,
            IJobDetail job,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public Task<IJobDetail> RetrieveJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return ExecuteWithoutLock(async conn =>
            {
                try
                {
                    return await RetrieveJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
                }
                catch (TypeLoadException e)
                {
                    throw new JobPersistenceException("Couldn't retrieve job because a required type was not found: " + e.Message, e);
                }
                catch (IOException e)
                {
                    throw new JobPersistenceException("Couldn't retrieve job because the BLOB couldn't be deserialized: " + e.Message, e);
                }
                catch (Exception e)
                {
                    throw new JobPersistenceException("Couldn't retrieve job: " + e.Message, e);
                }
            }, cancellationToken);
        }

        protected abstract Task<IJobDetail> RetrieveJob(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task<bool> RemoveJobs(
            IReadOnlyCollection<JobKey> jobKeys,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteInLock(
                LockType.TriggerAccess,
                async conn =>
                {
                    bool allFound = true;

                    // TODO: make this more efficient with a true bulk operation...
                    foreach (JobKey jobKey in jobKeys)
                    {
                        allFound = await RemoveJob(conn, jobKey, cancellationToken).ConfigureAwait(false) && allFound;
                    }

                    return allFound;
                }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<bool> RemoveJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(
                LockType.TriggerAccess,
                conn => RemoveJob(conn, jobKey, cancellationToken),
                cancellationToken);
        }

        private async Task<bool> RemoveJob(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            try
            {
                var jobTriggers = await GetTriggerNamesForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);

                foreach (TriggerKey jobTrigger in jobTriggers)
                {
                    await DeleteTriggerAndChildren(conn, jobTrigger, cancellationToken).ConfigureAwait(false);
                }

                return await DeleteJobAndChildren(conn, jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't remove job: {e.Message}", e);
            }
        }

        protected abstract Task<IReadOnlyCollection<TriggerKey>> GetTriggerNamesForJob(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <summary>
        /// Determines if a Trigger for the given job should be blocked.
        /// State can only transition to StatePausedBlocked/StateBlocked from
        /// StatePaused/StateWaiting respectively.
        /// </summary>
        /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
        protected virtual async Task<InternalTriggerState> CheckBlockedState(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            InternalTriggerState currentState,
            CancellationToken cancellationToken)
        {
            // State can only transition to BLOCKED from PAUSED or WAITING.
            if (currentState != InternalTriggerState.Waiting && currentState != InternalTriggerState.Paused)
            {
                return currentState;
            }

            try
            {
                var lst = await SelectFiredTriggerRecordsByJob(conn, jobKey, cancellationToken).ConfigureAwait(false);

                if (lst.Count > 0)
                {
                    FiredTriggerRecord rec = lst.First();
                    if (rec.JobDisallowsConcurrentExecution) // TODO: worry about failed/recovering/volatile job  states?
                    {
                        return InternalTriggerState.Paused == currentState
                            ? InternalTriggerState.PausedAndBlocked
                            : InternalTriggerState.Blocked;
                    }
                }

                return currentState;
            }
            catch (Exception e)
            {
                var message = $"Couldn't determine if trigger should be in a blocked state '{jobKey}': {e.Message}";
                throw new JobPersistenceException(message, e);
            }
        }

        protected abstract Task<IReadOnlyCollection<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
            TUnitOfWorkConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken);

        /// <summary>
        /// Execute the given callback in a transaction. Depending on the JobStore,
        /// the surrounding transaction may be assumed to be already present (managed).
        /// </summary>
        /// <remarks>This method just forwards to ExecuteInLock() with a null lockName.</remarks>
        protected Task<T> ExecuteWithoutLock<T>(
            Func<TUnitOfWorkConnection, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLock(LockType.None, txCallback, cancellationToken);
        }

        /// <summary>
        /// Execute the given callback in a transaction. Depending on the JobStore,
        /// the surrounding transaction may be assumed to be already present (managed).
        /// </summary>
        protected async Task ExecuteInLock(
            LockType lockName,
            Func<TUnitOfWorkConnection, Task> txCallback,
            CancellationToken cancellationToken = default)
        {
            await ExecuteInLock<object>(lockName, async conn =>
            {
                await txCallback(conn).ConfigureAwait(false);
                return null;
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute the given callback having acquired the given lock.
        /// Depending on the JobStore, the surrounding transaction may be
        /// assumed to be already present (managed).
        /// </summary>
        protected abstract Task<T> ExecuteInLock<T>(
            LockType lockName,
            Func<TUnitOfWorkConnection, Task<T>> txCallback,
            CancellationToken cancellationToken);
    }
}