using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Quartz.Core;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Simpl;
using Quartz.Util;

using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;

namespace Quartz.Impl.RavenDB
{
    /// <summary>
    /// An implementation of <see cref="IJobStore" /> to use RavenDB as a persistent job store.
    /// Provides an <see cref="IJob" /> and <see cref="ITrigger" /> storage mechanism for the
    /// <see cref="QuartzScheduler" />'s use.
    /// </summary>
    /// <remarks>
    /// Storage of <see cref="IJob" /> s and <see cref="ITrigger" /> s should be keyed
    /// on the combination of their name and group for uniqueness.
    /// </remarks>
    /// <seealso cref="QuartzScheduler" />
    /// <seealso cref="IJobStore" />
    /// <seealso cref="ITrigger" />
    /// <seealso cref="IJob" />
    /// <seealso cref="IJobDetail" />
    /// <seealso cref="JobDataMap" />
    /// <seealso cref="ICalendar" />
    /// <author>Iftah Ben Zaken</author>
    /// <author>Marko Lahma</author>
    public class RavenJobStore : PersistentJobStore<RavenConnection>, IClusterManagementOperations, IMisfireHandlerOperations
    {
        private IRavenLockHandler lockHandler;

        private DocumentStore documentStore;

        public override long EstimatedTimeToReleaseAndAcquireTrigger => 100;

        public static string Url { get; set; }
        public string Database { get; set; }


        public DateTimeOffset LastCheckin
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        protected override void ValidateInstanceName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('/') > -1)
            {
                throw new ArgumentException("scheduler name must be set and it cannot contain '/' character");
            }
        }

        public override async Task Initialize(
            ITypeLoadHelper typeLoadHelper,
            ISchedulerSignaler schedulerSignaler,
            CancellationToken cancellationToken = default)
        {
            await base.Initialize(typeLoadHelper, schedulerSignaler, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(Url))
            {
                throw new ConfigurationErrorsException("url is not defined");
            }

            if (string.IsNullOrWhiteSpace(Database))
            {
                throw new ConfigurationErrorsException("database is not defined");
            }

            documentStore = new DocumentStore
            {
                Urls = new[]
                {
                    Url
                },
                Database = Database
            };

            // TODO ideally optimize to better batch operations
            documentStore.Conventions.MaxNumberOfRequestsPerSession = Int32.MaxValue;
            documentStore.OnBeforeQuery += (sender, beforeQueryExecutedArgs) => { beforeQueryExecutedArgs.QueryCustomization.WaitForNonStaleResults(); };
            documentStore.Initialize();

            if (Clustered)
            {
                //Log.Info("Using db based data access locking (synchronization).");
                //lockHandler = new RavenLockHandler(InstanceName);
                throw new NotImplementedException();
            }
            else
            {
                Log.Info("Using thread monitor-based data access locking (synchronization).");
                lockHandler = new SimpleSemaphoreRavenLockHandler();
            }

            await new FiredTriggerIndex().ExecuteAsync(documentStore, token: cancellationToken, database: Database).ConfigureAwait(false);
            await new JobIndex().ExecuteAsync(documentStore, token: cancellationToken, database: Database).ConfigureAwait(false);
            await new TriggerIndex().ExecuteAsync(documentStore, token: cancellationToken, database: Database).ConfigureAwait(false);

            // If scheduler doesn't exist create new empty scheduler and store it
            var scheduler = new Scheduler
            {
                InstanceName = InstanceName,
                State = SchedulerState.Initialized
            };

            using (var session = documentStore.OpenAsyncSession())
            {
                await session.StoreAsync(scheduler, InstanceName, cancellationToken).ConfigureAwait(false);
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            await base.SchedulerStarted(cancellationToken).ConfigureAwait(false);
            await SetSchedulerState(SchedulerState.Started, cancellationToken).ConfigureAwait(false);
        }

        public override Task SchedulerPaused(CancellationToken cancellationToken = default)
        {
            return SetSchedulerState(SchedulerState.Paused, cancellationToken);
        }

        public override Task SchedulerResumed(CancellationToken cancellationToken = default)
        {
            return SetSchedulerState(SchedulerState.Resumed, cancellationToken);
        }

        public override async Task Shutdown(CancellationToken cancellationToken = default)
        {
            await base.Shutdown(cancellationToken).ConfigureAwait(false);

            await SetSchedulerState(SchedulerState.Shutdown, cancellationToken).ConfigureAwait(false);

            documentStore.Dispose();
            documentStore = null;
        }

        private async Task SetSchedulerState(SchedulerState state, CancellationToken cancellationToken = default)
        {
            using (var session = documentStore.OpenAsyncSession())
            {
                var scheduler = await session.LoadAsync<Scheduler>(InstanceName, cancellationToken).ConfigureAwait(false);
                scheduler.State = state;
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        protected override async Task<int> DeleteFiredTriggers(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var options = new QueryOperationOptions
            {
                AllowStale = false
            };
            var op = await documentStore.Operations.SendAsync(
                new DeleteByQueryOperation<FiredTrigger, FiredTriggerIndex>(x => x.Scheduler == InstanceName, options),
                token: cancellationToken).ConfigureAwait(false);

            var result = (BulkOperationResult) op.WaitForCompletion();
            return (int) result.Total;
        }

        protected override async Task<IReadOnlyCollection<TriggerKey>> GetTriggersInState(
            RavenConnection conn,
            InternalTriggerState state,
            CancellationToken cancellationToken)
        {
            var triggers = await conn.QueryTriggers()
                .Where(x => x.State == state)
                .Select(x => new
                {
                    x.Name,
                    x.Group
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var keys = new List<TriggerKey>();
            foreach (var trigger in triggers)
            {
                keys.Add(new TriggerKey(trigger.Name, trigger.Group));
            }
            return keys;
        }

        protected override async Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForRecoveringJobs(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var records = await conn.QueryFiredTriggers()
                .Where(x => x.SchedulerInstanceId == InstanceId && x.RequestsRecovery)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            List<IOperableTrigger> triggers = new List<IOperableTrigger>();
            List<FiredTriggerRecord> triggerData = new List<FiredTriggerRecord>();

            long dumId = SystemTime.UtcNow().Ticks;

            foreach (var record in records)
            {
                var jobKey = record.JobId.JobKeyFromDocumentId();
                int priority = record.Priority;
                DateTimeOffset firedTime = record.FiredTime;
                DateTimeOffset scheduledTime = record.ScheduledTime;

                SimpleTriggerImpl recoveryTrigger = new SimpleTriggerImpl(
                    "recover_" + InstanceId + "_" + dumId++,
                    SchedulerConstants.DefaultRecoveryGroup,
                    scheduledTime);

                recoveryTrigger.JobName = jobKey.Name;
                recoveryTrigger.JobGroup = jobKey.Group;
                recoveryTrigger.Priority = priority;
                recoveryTrigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;

                var dataHolder = new FiredTriggerRecord
                {
                    ScheduleTimestamp = scheduledTime,
                    FireTimestamp = firedTime
                };

                triggerData.Add(dataHolder);
                triggers.Add(recoveryTrigger);
            }

            // read JobDataMaps with different reader..
            var maps = await conn.QueryTriggers()
                .Where(x => x.Id.In(records.Select(r => r.TriggerId)))
                .Select(x => new
                    {
                        x.Id,
                        x.JobDataMap
                    }
                )
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var mapLookup = maps.ToDictionary(x => x.Id);

            for (int i = 0; i < triggers.Count; i++)
            {
                IOperableTrigger trigger = triggers[i];
                FiredTriggerRecord dataHolder = triggerData[i];

                var jd = new JobDataMap(mapLookup[trigger.Key.DocumentId(InstanceName)].JobDataMap);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerName, trigger.Key.Name);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerGroup, trigger.Key.Group);
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerFiretime, Convert.ToString(dataHolder.FireTimestamp, CultureInfo.InvariantCulture));
                jd.Put(SchedulerConstants.FailedJobOriginalTriggerScheduledFiretime, Convert.ToString(dataHolder.ScheduleTimestamp, CultureInfo.InvariantCulture));
                trigger.JobDataMap = jd;
            }

            return triggers;

        }

        protected override Task<int> UpdateTriggerStatesFromOtherStates(
            RavenConnection conn,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken )
        {
            var query = conn.QueryTriggers()
                .Where(x => x.State.In(oldStates));

            return SetTriggerStateForResults(query, newState, cancellationToken);
        }

        protected override async Task<TriggerStatus> GetTriggerStatus(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.QueryTrigger(triggerKey)
                .Select(x =>
                    new
                    {
                        x.Id,
                        x.JobId,
                        x.State,
                        x.NextFireTimeUtc
                    })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (trigger == null)
            {
                return null;
            }

            var status = new TriggerStatus(
                triggerKey,
                trigger.JobId.JobKeyFromDocumentId(),
                trigger.State,
                trigger.NextFireTimeUtc);

            return status;
        }

        protected override Task<int> UpdateTriggerStateFromOtherStates(
            RavenConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState[] oldStates,
            CancellationToken cancellationToken )
        {
            var query = conn.QueryTriggers()
                .Where(x => x.Id == triggerKey.DocumentId(InstanceName) && x.State.In(oldStates));

            return SetTriggerStateForResults(query, newState, cancellationToken);
        }

        protected override Task<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
            RavenConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            DateTimeOffset nextFireTime,
            CancellationToken cancellationToken )
        {
            var query = conn.QueryTriggers()
                .Where(x => x.Id == triggerKey.DocumentId(InstanceName) && x.State == oldState && x.NextFireTimeUtc == nextFireTime);

            return SetTriggerStateForResults(query, newState, cancellationToken);
        }

        protected override async Task<bool> GetMisfiredTriggersInWaitingState(
            RavenConnection conn,
            int count,
            List<TriggerKey> resultList,
            CancellationToken cancellationToken)
        {
            var triggers = await conn.QueryTriggers()
                .Where(x =>
                    x.MisfireInstruction != MisfireInstruction.IgnoreMisfirePolicy
                    && x.NextFireTimeUtc < MisfireTime
                    && x.State == InternalTriggerState.Waiting)
                .OrderBy(x => x.NextFireTimeUtc)
                .ThenByDescending(x => x.Priority)
                .Select(x => new
                {
                    x.Name,
                    x.Group
                })
                .Take(count != -1 ? count + 1 : count)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            bool hasMoreTriggersInStateThanRequested = false;
            foreach (var trigger in triggers)
            {
                if (resultList.Count == count)
                {
                    hasMoreTriggersInStateThanRequested = true;
                    break;
                }

                resultList.Add(new TriggerKey(trigger.Name, trigger.Group));
            }
            return hasMoreTriggersInStateThanRequested;
        }

        protected override IClusterManagementOperations ClusterManagementOperations => this;

        protected override IMisfireHandlerOperations MisfireHandlerOperations => this;

        protected override async Task DoStoreJob(
            RavenConnection conn,
            IJobDetail jobDetail,
            bool existingJob,
            CancellationToken cancellationToken)
        {
            if (existingJob)
            {
                var job = await conn.LoadJob(jobDetail.Key, cancellationToken).ConfigureAwait(false);
                job.UpdateWith(jobDetail);
            }
            else
            {
                var job = new Job(jobDetail, InstanceName);
                await conn.StoreAsync(job, job.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        protected override async Task<IReadOnlyCollection<TriggerKey>> GetTriggerNamesForJob(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            var triggers = await conn.QueryTriggers()
                .Where(x => x.JobId == jobKey.DocumentId(InstanceName))
                .Select(x => new
                {
                    x.Name,
                    x.Group
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<TriggerKey>(10);
            foreach (var trigger in triggers)
            {
                list.Add(new TriggerKey(trigger.Name, trigger.Group));
            }

            return list;
        }

        protected override async Task<IReadOnlyCollection<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            var triggers = await conn.QueryFiredTriggers()
                .Where(x => x.JobId == jobKey.DocumentId(InstanceName))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var records = new List<FiredTriggerRecord>(triggers.Count);
            foreach (var trigger in triggers)
            {
                FiredTriggerRecord rec = trigger.Deserialize();
                records.Add(rec);
            }
            return records;
        }

        protected override async Task<IJobDetail> RetrieveJob(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            var job = await conn.LoadJob(jobKey, cancellationToken).ConfigureAwait(false);
            return job?.Deserialize();
        }

        protected override async Task<bool> RemoveTrigger(
            RavenConnection conn,
            TriggerKey triggerKey,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            bool removedTrigger;
            try
            {
                var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);

                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                if (job == null)
                {
                    var dbJob = await conn.LoadJob(trigger.JobId, cancellationToken).ConfigureAwait(false);
                    job = dbJob.Deserialize();
                }

                removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                if (null != job && !job.Durable)
                {
                    var trigList = await GetTriggersForJob(job.Key, cancellationToken).ConfigureAwait(false);
                    if (trigList == null || trigList.Count == 0)
                    {
                        // Don't call RemoveJob() because we don't want to check for
                        // triggers again.
                        conn.Delete(trigger.JobId);
                        await SchedulerSignaler.NotifySchedulerListenersJobDeleted(job.Key, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't remove trigger: {e.Message}", e);
            }

            return removedTrigger;
        }

        protected override async Task<bool> DoReplaceTrigger(
            RavenConnection conn,
            TriggerKey triggerKey,
            IOperableTrigger newTrigger,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            bool replaced = trigger != null;

            await StoreTrigger(
                conn,
                newTrigger,
                job,
                replaceExisting: replaced,
                InternalTriggerState.Waiting,
                forceState: false,
                recovering: false,
                cancellationToken).ConfigureAwait(false);

            return replaced;
        }

        protected override async Task<IOperableTrigger> RetrieveTrigger(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            return trigger?.Deserialize();
        }

        protected override async Task AddPausedTriggerGroup(
            RavenConnection conn,
            string groupName,
            CancellationToken cancellationToken)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            scheduler.PausedTriggerGroups.Add(groupName);
        }

        protected override async Task<bool> IsTriggerGroupPaused(
            RavenConnection conn,
            string groupName,
            CancellationToken cancellationToken)
        {
            var pausedGroups = await GetSchedulerData(conn, x => x.PausedTriggerGroups, cancellationToken).ConfigureAwait(false);
            return pausedGroups.Contains(groupName);
        }

        protected override async Task StoreTrigger(
            RavenConnection conn,
            bool existingTrigger,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            Trigger entity;
            if (existingTrigger)
            {
                entity = await conn.LoadTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);
                entity.UpdateWith(trigger);
            }
            else
            {
                entity = new Trigger(trigger, InstanceName);
                await conn.StoreAsync(entity, entity.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        protected override async Task<bool> CalendarExists(
            RavenConnection conn,
            string calName,
            CancellationToken cancellationToken)
        {
            var calendars = await GetSchedulerData(conn, x => x.Calendars, cancellationToken).ConfigureAwait(false);
            return calendars.ContainsKey(calName);
        }

        protected override Task<bool> JobExists(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            return conn.ExistsAsync(jobKey.DocumentId(InstanceName));
        }

        protected override Task<bool> TriggerExists(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            return conn.ExistsAsync(triggerKey.DocumentId(InstanceName));
        }

        protected override async Task ClearAllSchedulingData(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            scheduler.Calendars.Clear();
            scheduler.PausedTriggerGroups.Clear();

            var options = new QueryOperationOptions {AllowStale = false};
            var op = await documentStore.Operations.SendAsync(
                new DeleteByQueryOperation<Trigger, TriggerIndex>(x => x.Scheduler == InstanceName, options),
                token: cancellationToken).ConfigureAwait(false);

            op.WaitForCompletion();

            op = await documentStore.Operations.SendAsync(
                new DeleteByQueryOperation<Job, JobIndex>(x => x.Scheduler == InstanceName, options),
                token: cancellationToken).ConfigureAwait(false);

            op.WaitForCompletion();

            op = await documentStore.Operations.SendAsync(
                new DeleteByQueryOperation<FiredTrigger, FiredTriggerIndex>(x => x.Scheduler == InstanceName, options),
                token: cancellationToken).ConfigureAwait(false);

            op.WaitForCompletion();
        }

        protected override async Task StoreCalendar(
            RavenConnection conn,
            string name,
            ICalendar calendar,
            bool replaceExisting,
            bool updateTriggers,
            CancellationToken cancellationToken)
        {
            var calendarCopy = calendar.Clone();
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);

            if (scheduler.Calendars.ContainsKey(name) && !replaceExisting)
            {
                throw new ObjectAlreadyExistsException($"Calendar with name '{name}' already exists.");
            }

            // add or replace calendar
            scheduler.Calendars[name] = calendarCopy;

            if (!updateTriggers)
            {
                return;
            }

            var triggers = await conn.QueryTriggers()
                .Where(t => t.CalendarName == name)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var triggerToUpdate in triggers)
            {
                var trigger = triggerToUpdate.Deserialize();
                trigger.UpdateWithNewCalendar(calendarCopy, MisfireThreshold);
                triggerToUpdate.UpdateFireTimes(trigger);
            }
        }

        protected override Task UpdateTriggerStatesForJobFromOtherState(
            RavenConnection conn,
            JobKey jobKey,
            InternalTriggerState newState,
            InternalTriggerState oldState,
            CancellationToken cancellationToken)
        {
            var query = conn.QueryTriggers()
                .Where(x => x.JobId == jobKey.DocumentId(InstanceName) && x.State == oldState);

            return SetTriggerStateForResults(query, newState, cancellationToken);
        }

        protected override async Task UpdateJobData(
            RavenConnection conn,
            IJobDetail jobDetail,
            CancellationToken cancellationToken)
        {
            var job = await conn.LoadJob(jobDetail.Key, cancellationToken).ConfigureAwait(false);
            job.JobDataMap = jobDetail.JobDataMap;
        }

        protected override Task DeleteFiredTrigger(
            RavenConnection conn,
            string triggerFireInstanceId,
            CancellationToken cancellationToken)
        {
            conn.Delete(triggerFireInstanceId);
            return Task.CompletedTask;
        }

        protected override Task UpdateTriggerStatesForJob(
            RavenConnection conn,
            JobKey triggerJobKey,
            InternalTriggerState state,
            CancellationToken cancellationToken)
        {
            var query = conn.QueryTriggers()
                .Where(x => x.JobId == triggerJobKey.DocumentId(InstanceName));

            return SetTriggerStateForResults(query, state, cancellationToken);
        }

        private static async Task<int> SetTriggerStateForResults(
            IRavenQueryable<Trigger> query,
            InternalTriggerState state,
            CancellationToken cancellationToken)
        {
            var triggers = await query
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var trigger in triggers)
            {
                trigger.State = state;
            }

            return triggers.Count;
        }

        protected override async Task UpdateTriggerState(
            RavenConnection conn,
            TriggerKey triggerKey,
            InternalTriggerState state,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            trigger.State = state;
        }

        protected override async Task<bool> RemoveCalendar(
            RavenConnection conn,
            string calName,
            CancellationToken cancellationToken)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            return scheduler.Calendars.Remove(calName);
        }

        protected override async Task<ICalendar> RetrieveCalendar(
            RavenConnection conn,
            string calName,
            CancellationToken cancellationToken)
        {
            var calendars = await GetSchedulerData(conn, x => x.Calendars, cancellationToken).ConfigureAwait(false);
            calendars.TryGetValue(calName, out var calendar);
            return calendar;
        }

        protected override Task<int> GetNumberOfJobs(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            return conn.QueryJobs().CountAsync(cancellationToken);
        }

        protected override Task<int> GetNumberOfTriggers(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            return conn.QueryTriggers().CountAsync(cancellationToken);
        }

        protected override async Task<int> GetNumberOfCalendars(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var calendars = await GetSchedulerData(conn, x => x.Calendars, cancellationToken).ConfigureAwait(false);
            return calendars.Count;
        }

        protected override async Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            RavenConnection conn,
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken)
        {
            StringOperator op = matcher.CompareWithOperator;
            string compareToValue = matcher.CompareToValue;

            var result = new HashSet<JobKey>();

            {
                var allJobs = await conn.QueryJobs()
                    .Select(x => new
                    {
                        x.Name,
                        x.Group
                    })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                foreach (var job in allJobs)
                {
                    if (op.Evaluate(job.Group, compareToValue))
                    {
                        result.Add(new JobKey(job.Name, job.Group));
                    }
                }
            }

            return result;
        }

        protected override async Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            RavenConnection conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            StringOperator op = matcher.CompareWithOperator;
            string compareToValue = matcher.CompareToValue;

            var result = new HashSet<TriggerKey>();
            var allTriggers = await conn.QueryTriggers()
                .Select(x => new
                {
                    x.Name,
                    x.Group
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var trigger in allTriggers)
            {
                if (op.Evaluate(trigger.Group, compareToValue))
                {
                    result.Add(new TriggerKey(trigger.Name, trigger.Group));
                }
            }

            return result;
        }

        protected override Task<IReadOnlyCollection<string>> GetJobGroupNames(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            return ExtractGroup(conn, conn.QueryJobs(), cancellationToken);
        }

        protected override Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            return GetTriggerGroupNames(conn, GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken);
        }

        private Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            RavenConnection conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            var query = conn.QueryTriggers();

            query = query.WhereMatches(matcher);

            return ExtractGroup(conn, query, cancellationToken);
        }

        protected override async Task<IReadOnlyCollection<string>> GetCalendarNames(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var calendars = await GetSchedulerData(conn, x => x.Calendars, cancellationToken).ConfigureAwait(false);
            return calendars.Keys;
        }

        protected override async Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            string jobId = jobKey.DocumentId(InstanceName);
            var triggers = await conn.QueryTriggers()
                .Where(x => x.JobId == jobId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var result = triggers
                .Select(x => x.Deserialize())
                .ToList();

            return result;
        }

        protected override async Task<TriggerState> GetTriggerState(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);

            if (trigger == null)
            {
                return TriggerState.None;
            }

            switch (trigger.State)
            {
                case InternalTriggerState.Complete:
                    return TriggerState.Complete;
                case InternalTriggerState.Paused:
                    return TriggerState.Paused;
                case InternalTriggerState.PausedAndBlocked:
                    return TriggerState.Paused;
                case InternalTriggerState.Blocked:
                    return TriggerState.Blocked;
                case InternalTriggerState.Error:
                    return TriggerState.Error;
                default:
                    return TriggerState.Normal;
            }
        }

        protected override async Task<InternalTriggerState> GetInternalTriggerState(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            return trigger.State;
        }

        protected override async Task<IReadOnlyCollection<string>> ResumeTriggers(
            RavenConnection conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            foreach (var triggerGroup in scheduler.PausedTriggerGroups.ToList())
            {
                if (matcher.CompareWithOperator.Evaluate(triggerGroup, matcher.CompareToValue))
                {
                    scheduler.PausedTriggerGroups.Remove(triggerGroup);
                }
            }

            var groups = new ReadOnlyCompatibleHashSet<string>();
            var keys = await GetTriggerKeys(matcher, cancellationToken).ConfigureAwait(false);

            foreach (TriggerKey triggerKey in keys)
            {
                await ResumeTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                groups.Add(triggerKey.Group);
            }

            return groups;
        }

        protected override Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            return GetSchedulerData<IReadOnlyCollection<string>>(conn, x => x.PausedTriggerGroups, cancellationToken);
        }

        private async Task<T> GetSchedulerData<T>(
            RavenConnection conn,
            Func<Scheduler, T> extractor,
            CancellationToken cancellationToken = default)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            return extractor(scheduler);
        }

        protected override async Task ClearAllTriggerGroupsPausedFlag(
            RavenConnection conn,
            CancellationToken cancellationToken)
        {
            var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
            scheduler.PausedTriggerGroups.Remove(AllGroupsPaused);
        }

        protected override async Task<IReadOnlyCollection<TriggerKey>> GetTriggerToAcquire(
            RavenConnection conn,
            DateTimeOffset noLaterThan,
            DateTimeOffset noEarlierThan,
            int maxCount,
            CancellationToken cancellationToken)
        {
            if (maxCount < 1)
            {
                maxCount = 1; // we want at least one trigger back.
            }

            var triggers = await conn.QueryTriggers()
                .Where(x =>
                    x.State == InternalTriggerState.Waiting
                    && x.NextFireTimeUtc <= noLaterThan
                    && (x.MisfireInstruction == -1 || (x.MisfireInstruction != -1 && x.NextFireTimeUtc >= noEarlierThan)))
                .OrderBy(x => x.NextFireTimeUtc)
                .ThenByDescending(x => x.Priority)
                .Select(x => new
                {
                    x.Name,
                    x.Group
                })
                .Take(maxCount)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var nextTriggers = new List<TriggerKey>(triggers.Count);
            foreach (var trigger in triggers)
            {
                nextTriggers.Add(new TriggerKey(trigger.Name, trigger.Group));
            }
            return nextTriggers;
        }

        protected override Task AddFiredTrigger(
            RavenConnection conn,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail jobDetail,
            CancellationToken cancellationToken)
        {
            var firedTrigger = new FiredTrigger(trigger.FireInstanceId, InstanceName)
            {
                SchedulerInstanceId = InstanceId,
                TriggerId = trigger.Key.DocumentId(InstanceName),
                FiredTime = SystemTime.UtcNow(),
                ScheduledTime = trigger.GetNextFireTimeUtc() ?? DateTimeOffset.MinValue,
                State = state,
                JobId = jobDetail?.Key.DocumentId(InstanceName),
                IsNonConcurrent = jobDetail?.ConcurrentExecutionDisallowed ?? false,
                RequestsRecovery = jobDetail?.RequestsRecovery ?? false,
                Priority = trigger.Priority
            };

            return conn.StoreAsync(firedTrigger, firedTrigger.Id, cancellationToken);
        }

        protected override async Task<IJobDetail> GetJobForTrigger(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var jobId = await conn.QueryTriggers()
                .Where(x => x.Id == triggerKey.DocumentId(InstanceName))
                .Select(x => x.JobId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return jobId != null
                ? (await conn.LoadJob(jobId, cancellationToken).ConfigureAwait(false))?.Deserialize()
                : null;
        }

        protected override async Task<bool> DeleteTriggerAndChildren(
            RavenConnection conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken)
        {
            var trigger = await conn.LoadTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            if (trigger != null)
            {
                conn.Delete(trigger);
            }
            return trigger != null;
        }

        protected override async Task<bool> DeleteJobAndChildren(
            RavenConnection conn,
            JobKey jobKey,
            CancellationToken cancellationToken)
        {
            var job = await conn.LoadJob(jobKey, cancellationToken).ConfigureAwait(false);
            if (job != null)
            {
                conn.Delete(job);
            }
            return job != null;
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// </summary>
        protected override async Task<IReadOnlyCollection<string>> PauseTriggerGroup(
            RavenConnection conn,
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken)
        {
            try
            {
                var triggers = await conn.QueryTriggers()
                    .WhereMatches(matcher)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var groups = new HashSet<string>();
                foreach (var trigger in triggers)
                {
                    groups.Add(trigger.Group);
                    switch (trigger.State)
                    {
                        case InternalTriggerState.Waiting:
                        case InternalTriggerState.Acquired:
                            trigger.State = InternalTriggerState.Paused;
                            break;
                        case InternalTriggerState.Blocked:
                            trigger.State = InternalTriggerState.PausedAndBlocked;
                            break;
                    }
                }

                // make sure to account for an exact group match for a group that doesn't yet exist
                StringOperator op = matcher.CompareWithOperator;
                if (op.Equals(StringOperator.Equality) && !groups.Contains(matcher.CompareToValue))
                {
                    groups.Add(matcher.CompareToValue);
                }

                var scheduler = await conn.LoadScheduler(cancellationToken).ConfigureAwait(false);
                foreach (string group in groups)
                {
                    scheduler.PausedTriggerGroups.Add(group);
                }

                return new ReadOnlyCompatibleHashSet<string>(groups);
            }
            catch (Exception e)
            {
                throw new JobPersistenceException($"Couldn't pause trigger group '{matcher}': {e.Message}", e);
            }
        }

        protected override async Task UpdateFiredTrigger(
            RavenConnection conn,
            IOperableTrigger trigger,
            InternalTriggerState state,
            IJobDetail job,
            CancellationToken cancellationToken)
        {
            var ft = await conn.LoadFiredTrigger(trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
            ft.FiredTime = SystemTime.UtcNow();
            ft.ScheduledTime = trigger.GetNextFireTimeUtc() ?? DateTimeOffset.MinValue;
            ft.State = state;
            ft.SchedulerInstanceId = InstanceId;
            ft.JobId = job?.Key.DocumentId(InstanceName);
            ft.IsNonConcurrent = job?.ConcurrentExecutionDisallowed ?? false;
            ft.RequestsRecovery = job?.RequestsRecovery ?? false;
        }

        protected override async Task<IReadOnlyCollection<FiredTriggerRecord>> GetInstancesFiredTriggerRecords(
            RavenConnection conn,
            string instanceId,
            CancellationToken cancellationToken)
        {
            var firedTriggers = await conn.QueryFiredTriggers()
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var records = new List<FiredTriggerRecord>(firedTriggers.Count);
            foreach (var firedTrigger in firedTriggers)
            {
                records.Add(firedTrigger.Deserialize());
            }

            return records;
        }

        protected override Task<T> ExecuteInLock<T>(
            LockType lockType,
            Func<RavenConnection, Task<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInNonManagedTXLock(lockType, txCallback, cancellationToken);
        }

        protected override async Task<T> ExecuteInNonManagedTXLock<T>(
            LockType lockType,
            Func<RavenConnection, Task<T>> txCallback,
            Func<RavenConnection, T, Task<bool>> txValidator,
            CancellationToken cancellationToken)
        {
            bool transOwner = false;
            Guid requestorId = Guid.NewGuid();
            RavenConnection conn = null;
            try
            {
                conn = new RavenConnection(documentStore.OpenAsyncSession(), InstanceName);
                if (lockType != LockType.None)
                {
                    transOwner = await lockHandler.ObtainLock(requestorId, conn, lockType, cancellationToken).ConfigureAwait(false);
                }

                T result = await txCallback(conn).ConfigureAwait(false);

                try
                {
                    await conn.Commit(cancellationToken).ConfigureAwait(false);
                }
                catch (JobPersistenceException)
                {
                    conn.Rollback();
                    if (txValidator == null)
                    {
                        throw;
                    }
                    if (!await RetryExecuteInNonManagedTXLock(
                        lockType,
                        async connection => await txValidator(connection, result).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false))
                    {
                        throw;
                    }
                }

                DateTimeOffset? sigTime = conn.SignalSchedulingChangeOnTxCompletion;
                if (sigTime != null)
                {
                    await SchedulerSignaler.SignalSchedulingChange(sigTime, CancellationToken.None).ConfigureAwait(false);
                }

                return result;
            }
            catch (JobPersistenceException)
            {
                conn?.Rollback();
                throw;
            }
            catch (Exception e)
            {
                conn?.Rollback();
                throw new JobPersistenceException("Unexpected runtime exception: " + e.Message, e);
            }
            finally
            {
                if (transOwner)
                {
                    try
                    {
                        if (lockType != LockType.None)
                        {
                            await lockHandler.ReleaseLock(requestorId, lockType, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        conn?.Dispose();
                    }
                }
            }
        }

        public Task<bool> CheckCluster(Guid requestorId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        async Task<RecoverMisfiredJobsResult> IMisfireHandlerOperations.RecoverMisfires(
            Guid requestorId,
            CancellationToken cancellationToken)
        {
            bool transOwner = false;
            var conn = new RavenConnection(documentStore.OpenAsyncSession(), InstanceName);
            try
            {
                RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;

                // Before we make the potentially expensive call to acquire the
                // trigger lock, peek ahead to see if it is likely we would find
                // misfired triggers requiring recovery.
                int misfireCount = DoubleCheckLockMisfireHandler
                    ? await CountMisfiredTriggersInState(conn, InternalTriggerState.Waiting, MisfireTime, cancellationToken).ConfigureAwait(false)
                    : int.MaxValue;

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Found {0} triggers that missed their scheduled fire-time.", misfireCount);
                }

                if (misfireCount > 0)
                {
                    transOwner = await lockHandler.ObtainLock(requestorId, conn, LockType.TriggerAccess, cancellationToken).ConfigureAwait(false);

                    result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                }

                await conn.Commit(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (JobPersistenceException)
            {
                conn.Rollback();
                throw;
            }
            catch (Exception e)
            {
                conn.Rollback();
                throw new JobPersistenceException("Database error recovering from misfires.", e);
            }
            finally
            {
                if (transOwner)
                {
                    try
                    {
                        await lockHandler.ReleaseLock(requestorId, LockType.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        conn.Dispose();
                    }
                }
            }
        }

        private static Task<int> CountMisfiredTriggersInState(
            RavenConnection conn,
            InternalTriggerState state,
            DateTimeOffset misfireTime,
            CancellationToken cancellationToken)
        {
            return conn.QueryTriggers()
                .Where(x =>
                    x.MisfireInstruction != MisfireInstruction.IgnoreMisfirePolicy
                    && x.NextFireTimeUtc < misfireTime
                    && x.State == state)
                .CountAsync(cancellationToken);
        }

        // TODO http://issues.hibernatingrhinos.com/issue/RavenDB-11668
        private static async Task<IReadOnlyCollection<string>> ExtractGroup<T>(
            RavenConnection conn,
            IRavenQueryable<T> query,
            CancellationToken cancellationToken) where T : IHasGroup
        {
            var queryString = query.ToString();

            var distinct = query.Select(x => x.Group).Distinct();
            if (!distinct.ToString().EndsWith("'Group'", StringComparison.Ordinal))
            {
                // no workaround needed
                return await distinct.ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            var idx1 = queryString.IndexOf('\'');
            var idx2 = queryString.IndexOf('\'', idx1 + 1) + 1;

            // build a better query
            var newQuery = queryString.Substring(0, idx2);
            newQuery += " as idx" + queryString.Substring(idx2);
            newQuery += " select distinct idx.Group";

            var rawDocumentQuery = conn.RawQuery<JObject>(newQuery);

            // extract parameters that should be used
            var provider = (IRavenQueryProvider) query.Provider;
            var documentQuery = provider.ToAsyncDocumentQuery<T>(query.Expression);

            var queryParametersField = documentQuery.GetType().GetField("QueryParameters", BindingFlags.Instance | BindingFlags.NonPublic);
            var queryParameters = (Raven.Client.Parameters) queryParametersField.GetValue(documentQuery);

            foreach (var pair in queryParameters)
            {
                rawDocumentQuery.AddParameter(pair.Key, pair.Value);
            }

            var jObjects = await rawDocumentQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            var returnValue = new List<string>(jObjects.Count);
            foreach (var o in jObjects)
            {
                returnValue.Add(o["Group"].Value<string>());
            }

            return returnValue;
        }

    }
}