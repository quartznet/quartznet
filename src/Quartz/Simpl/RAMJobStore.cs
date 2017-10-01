#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Matchers;
using Quartz.Logging;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Simpl
{
    /// <summary>
    /// This class implements a <see cref="IJobStore" /> that
    /// utilizes RAM as its storage device.
    /// <para>
    /// As you should know, the ramification of this is that access is extremely
    /// fast, but the data is completely volatile - therefore this <see cref="IJobStore" />
    /// should not be used if true persistence between program shutdowns is
    /// required.
    /// </para>
    /// </summary>
    /// <author>James House</author>
    /// <author>Sharada Jambula</author>
    /// <author>Marko Lahma (.NET)</author>
    public class RAMJobStore : IJobStore
    {
        private readonly object lockObject = new object();

        private readonly ConcurrentDictionary<JobKey, JobWrapper> jobsByKey = new ConcurrentDictionary<JobKey, JobWrapper>();
        private readonly ConcurrentDictionary<TriggerKey, TriggerWrapper> triggersByKey = new ConcurrentDictionary<TriggerKey, TriggerWrapper>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<JobKey, JobWrapper>> jobsByGroup = new ConcurrentDictionary<string, ConcurrentDictionary<JobKey, JobWrapper>>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<TriggerKey, TriggerWrapper>> triggersByGroup = new ConcurrentDictionary<string, ConcurrentDictionary<TriggerKey, TriggerWrapper>>();
        private readonly SortedSet<TriggerWrapper> timeTriggers = new SortedSet<TriggerWrapper>(new TriggerWrapperComparator());
        private readonly ConcurrentDictionary<string, ICalendar> calendarsByName = new ConcurrentDictionary<string, ICalendar>();
        private readonly Dictionary<JobKey, List<TriggerWrapper>> triggersByJob = new Dictionary<JobKey, List<TriggerWrapper>>(1000);
        private readonly HashSet<string> pausedTriggerGroups = new HashSet<string>();
        private readonly HashSet<string> pausedJobGroups = new HashSet<string>();
        private readonly HashSet<JobKey> blockedJobs = new HashSet<JobKey>();
        private TimeSpan misfireThreshold = TimeSpan.FromSeconds(5);
        private ISchedulerSignaler signaler;

        /// <summary>
        /// Initializes a new instance of the <see cref="RAMJobStore"/> class.
        /// </summary>
        public RAMJobStore()
        {
            Log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// The time span by which a trigger must have missed its
        /// next-fire-time, in order for it to be considered "misfired" and thus
        /// have its misfire instruction applied.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public virtual TimeSpan MisfireThreshold
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

        private static long ftrCtr = SystemTime.UtcNow().Ticks;

        /// <summary>
        /// Gets the fired trigger record id.
        /// </summary>
        /// <returns>The fired trigger record id.</returns>
        protected virtual string GetFiredTriggerRecordId()
        {
            long value = Interlocked.Increment(ref ftrCtr);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        public virtual Task Initialize(
            ITypeLoadHelper loadHelper,
            ISchedulerSignaler signaler,
            CancellationToken cancellationToken = default)
        {
            this.signaler = signaler;
            Log.Info("RAMJobStore initialized.");
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// the scheduler has started.
        /// </summary>
        public virtual Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            // nothing to do
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the JobStore that
        /// the scheduler has been paused.
        /// </summary>
        public Task SchedulerPaused(CancellationToken cancellationToken = default)
        {
            // nothing to do
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the JobStore that
        /// the scheduler has resumed after being paused.
        /// </summary>
        public Task SchedulerResumed(CancellationToken cancellationToken = default)
        {
            // nothing to do
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
        /// it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
        {
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Returns whether this instance supports persistence.
        /// </summary>
        /// <value></value>
        /// <returns></returns>
        public virtual bool SupportsPersistence => false;

        /// <summary>
        /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
        /// <see cref="ICalendar"/>s.
        /// </summary>
        public Task ClearAllSchedulingData(CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                // unschedule jobs (delete triggers)
                foreach (string group in triggersByGroup.Keys)
                {
                    var keys = GetTriggerKeysInternal(GroupMatcher<TriggerKey>.GroupEquals(group));
                    foreach (TriggerKey key in keys)
                    {
                        RemoveTriggerInternal(key);
                    }
                }
                // delete jobs
                foreach (string group in jobsByGroup.Keys)
                {
                    var keys = GetJobKeysInternal(GroupMatcher<JobKey>.GroupEquals(group));
                    foreach (JobKey key in keys)
                    {
                        RemoveJobInternal(key);
                    }
                }
                // delete calendars
                foreach (string name in calendarsByName.Keys)
                {
                    RemoveCalendarInternal(name);
                }
            }

            return TaskUtil.CompletedTask;
        }

        private ILog Log { get; }

        /// <summary>
        /// Store the given <see cref="IJobDetail" /> and <see cref="ITrigger" />.
        /// </summary>
        /// <param name="newJob">The <see cref="IJobDetail" /> to be stored.</param>
        /// <param name="newTrigger">The <see cref="ITrigger" /> to be stored.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task StoreJobAndTrigger(
            IJobDetail newJob,
            IOperableTrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            StoreJobInternal(newJob, false);
            StoreTriggerInternal(newTrigger, false);
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Returns true if the given job group is paused.
        /// </summary>
        /// <param name="groupName">Job group name</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns></returns>
        public virtual Task<bool> IsJobGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(pausedJobGroups.Contains(groupName));
        }

        /// <summary>
        /// Returns true if the given TriggerGroup is paused.
        /// </summary>
        /// <returns></returns>
        public virtual Task<bool> IsTriggerGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(pausedTriggerGroups.Contains(groupName));
        }

        /// <summary>
        /// Store the given <see cref="IJob" />.
        /// </summary>
        /// <param name="newJob">The <see cref="IJob" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="IJob" /> existing in the
        /// <see cref="IJobStore" /> with the same name and group should be
        /// over-written.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task StoreJob(
            IJobDetail newJob,
            bool replaceExisting,
            CancellationToken cancellationToken = default)
        {
            StoreJobInternal(newJob, replaceExisting);
            return TaskUtil.CompletedTask;
        }

        private void StoreJobInternal(IJobDetail newJob, bool replaceExisting)
        {
            lock (lockObject)
            {
                JobWrapper jw = new JobWrapper(newJob.Clone());

                bool repl = false;

                if (jobsByKey.ContainsKey(jw.Key))
                {
                    if (!replaceExisting)
                    {
                        throw new ObjectAlreadyExistsException(newJob);
                    }
                    repl = true;
                }

                if (!repl)
                {
                    // get job group
                    if (!jobsByGroup.TryGetValue(newJob.Key.Group, out var grpMap))
                    {
                        grpMap = new ConcurrentDictionary<JobKey, JobWrapper>();
                        jobsByGroup[newJob.Key.Group] = grpMap;
                    }
                    // add to jobs by group
                    grpMap[newJob.Key] = jw;
                    // add to jobs by FQN map
                    jobsByKey[jw.Key] = jw;
                }
                else
                {
                    // update job detail
                    JobWrapper orig = jobsByKey[jw.Key];
                    orig.JobDetail = jw.JobDetail;
                }
            }
        }

        /// <summary>
        /// Remove (delete) the <see cref="IJob" /> with the given
        /// name, and any <see cref="ITrigger" /> s that reference
        /// it.
        /// </summary>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="IJob" /> with the given name and
        /// group was found and removed from the store.
        /// </returns>
        public virtual Task<bool> RemoveJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RemoveJobInternal(jobKey));
        }

        private bool RemoveJobInternal(JobKey jobKey)
        {
            lock (lockObject)
            {
                bool found = false;
                var triggersForJob = GetTriggersForJobInternal(jobKey);
                foreach (IOperableTrigger trigger in triggersForJob)
                {
                    RemoveTriggerInternal(trigger.Key);
                    found = true;
                }

                jobsByKey.TryRemove(jobKey, out var tempObject);
                found = tempObject != null || found;
                if (found)
                {
                    jobsByGroup.TryGetValue(jobKey.Group, out var grpMap);
                    if (grpMap != null)
                    {
                        grpMap.TryRemove(jobKey, out tempObject);
                        if (grpMap.Count == 0)
                        {
                            jobsByGroup.TryRemove(jobKey.Group, out _);
                        }
                    }
                }
                return found;
            }
        }

        public Task<bool> RemoveJobs(
            IReadOnlyCollection<JobKey> jobKeys,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                bool allFound = true;
                foreach (JobKey key in jobKeys)
                {
                    allFound = RemoveJobInternal(key) && allFound;
                }
                return Task.FromResult(allFound);
            }
        }

        public Task<bool> RemoveTriggers(
            IReadOnlyCollection<TriggerKey> triggerKeys,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                bool allFound = true;
                foreach (TriggerKey key in triggerKeys)
                {
                    allFound = RemoveTriggerInternal(key) && allFound;
                }
                return Task.FromResult(allFound);
            }
        }

        public Task StoreJobsAndTriggers(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                // make sure there are no collisions...
                if (!replace)
                {
                    foreach (IJobDetail job in triggersAndJobs.Keys)
                    {
                        if (jobsByKey.ContainsKey(job.Key))
                        {
                            throw new ObjectAlreadyExistsException(job);
                        }
                        foreach (ITrigger trigger in triggersAndJobs[job])
                        {
                            if (triggersByKey.ContainsKey(trigger.Key))
                            {
                                throw new ObjectAlreadyExistsException(trigger);
                            }
                        }
                    }
                }
                // do bulk add...
                foreach (IJobDetail job in triggersAndJobs.Keys)
                {
                    StoreJobInternal(job, true);
                    foreach (ITrigger trigger in triggersAndJobs[job])
                    {
                        StoreTriggerInternal((IOperableTrigger) trigger, true);
                    }
                }
            }

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Remove (delete) the <see cref="ITrigger" /> with the
        /// given name.
        /// </summary>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
        public virtual Task<bool> RemoveTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return RemoveTrigger(triggerKey, true);
        }

        /// <summary>
        /// Store the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="newTrigger">The <see cref="ITrigger" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ITrigger" /> existing in
        /// the <see cref="IJobStore" /> with the same name and group should
        /// be over-written.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task StoreTrigger(
            IOperableTrigger newTrigger,
            bool replaceExisting,
            CancellationToken cancellationToken = default)
        {
            StoreTriggerInternal(newTrigger, replaceExisting);
            return TaskUtil.CompletedTask;
        }

        private void StoreTriggerInternal(
            IOperableTrigger newTrigger,
            bool replaceExisting)
        {
            lock (lockObject)
            {
                TriggerWrapper tw = new TriggerWrapper((IOperableTrigger) newTrigger.Clone());
                if (triggersByKey.TryGetValue(tw.TriggerKey, out _))
                {
                    if (!replaceExisting)
                    {
                        throw new ObjectAlreadyExistsException(newTrigger);
                    }

                    // don't delete orphaned job, this trigger has the job anyways
                    RemoveTriggerInternal(newTrigger.Key, removeOrphanedJob: false);
                }

                if (RetrieveJobInternal(newTrigger.JobKey) == null)
                {
                    throw new JobPersistenceException("The job (" + newTrigger.JobKey +
                                                      ") referenced by the trigger does not exist.");
                }

                // add to triggers by job
                if (!triggersByJob.TryGetValue(tw.JobKey, out var jobList))
                {
                    jobList = new List<TriggerWrapper>(1);
                    triggersByJob.Add(tw.JobKey, jobList);
                }
                jobList.Add(tw);

                // add to triggers by group
                triggersByGroup.TryGetValue(newTrigger.Key.Group, out var grpMap);

                if (grpMap == null)
                {
                    grpMap = new ConcurrentDictionary<TriggerKey, TriggerWrapper>();
                    triggersByGroup[newTrigger.Key.Group] = grpMap;
                }
                grpMap[newTrigger.Key] = tw;
                // add to triggers by FQN map
                triggersByKey[tw.TriggerKey] = tw;

                if (pausedTriggerGroups.Contains(newTrigger.Key.Group) || pausedJobGroups.Contains(newTrigger.JobKey.Group))
                {
                    tw.state = InternalTriggerState.Paused;
                    if (blockedJobs.Contains(tw.JobKey))
                    {
                        tw.state = InternalTriggerState.PausedAndBlocked;
                    }
                }
                else if (blockedJobs.Contains(tw.JobKey))
                {
                    tw.state = InternalTriggerState.Blocked;
                }
                else
                {
                    timeTriggers.Add(tw);
                }
            }
        }

        /// <summary>
        /// Remove (delete) the <see cref="ITrigger" /> with the
        /// given name.
        ///
        /// </summary>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
        /// <param name="key">The <see cref="ITrigger" /> to be removed.</param>
        /// <param name="removeOrphanedJob">Whether to delete orphaned job details from scheduler if job becomes orphaned from removing the trigger.</param>
        protected virtual Task<bool> RemoveTrigger(
            TriggerKey key,
            bool removeOrphanedJob)
        {
            return Task.FromResult(RemoveTriggerInternal(key, removeOrphanedJob));
        }

        private bool RemoveTriggerInternal(TriggerKey key, bool removeOrphanedJob = true)
        {
            lock (lockObject)
            {
                // remove from triggers by FQN map
                var found = triggersByKey.TryRemove(key, out var tw);
                if (found)
                {
                    // remove from triggers by group
                    if (triggersByGroup.TryGetValue(key.Group, out var grpMap))
                    {
                        grpMap.TryRemove(key, out tw);
                        if (grpMap.Count == 0)
                        {
                            triggersByGroup.TryRemove(key.Group, out _);
                        }
                    }
                    //remove from triggers by job
                    if (triggersByJob.TryGetValue(tw.JobKey, out var jobList))
                    {
                        jobList.Remove(tw);
                        if (jobList.Count == 0)
                        {
                            triggersByJob.Remove(tw.JobKey);
                        }
                    }

                    timeTriggers.Remove(tw);

                    if (removeOrphanedJob)
                    {
                        JobWrapper jw = jobsByKey[tw.JobKey];
                        var trigs = GetTriggersForJobInternal(tw.JobKey);
                        if ((trigs == null || trigs.Count == 0) && !jw.JobDetail.Durable)
                        {
                            if (RemoveJobInternal(jw.Key))
                            {
                                signaler.NotifySchedulerListenersJobDeleted(jw.Key).ConfigureAwait(false).GetAwaiter().GetResult();
                            }
                        }
                    }
                }
                return found;
            }
        }

        /// <summary>
        /// Replaces the trigger.
        /// </summary>
        /// <param name="triggerKey">The <see cref="TriggerKey"/> of the <see cref="ITrigger" /> to be replaced.</param>
        /// <param name="newTrigger">The new trigger.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task<bool> ReplaceTrigger(
            TriggerKey triggerKey,
            IOperableTrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            bool found;

            lock (lockObject)
            {
                // remove from triggers by FQN map
                triggersByKey.TryRemove(triggerKey, out var tw);
                found = tw != null;

                if (found)
                {
                    if (!tw.Trigger.JobKey.Equals(newTrigger.JobKey))
                    {
                        throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                    }

                    // remove from triggers by group
                    triggersByGroup.TryGetValue(triggerKey.Group, out var grpMap);

                    if (grpMap != null)
                    {
                        grpMap.TryRemove(triggerKey, out _);
                        if (grpMap.Count == 0)
                        {
                            triggersByGroup.TryRemove(triggerKey.Group, out grpMap);
                        }
                    }

                    // remove from triggers by job
                    if (triggersByJob.TryGetValue(tw.JobKey, out var jobList))
                    {
                        jobList.Remove(tw);
                        if (jobList.Count == 0)
                        {
                            triggersByJob.Remove(tw.JobKey);
                        }
                    }

                    timeTriggers.Remove(tw);

                    try
                    {
                        StoreTriggerInternal(newTrigger, replaceExisting: false);
                    }
                    catch (JobPersistenceException)
                    {
                        StoreTriggerInternal(tw.Trigger, replaceExisting: false); // put previous trigger back...
                        throw;
                    }
                }
            }
            return Task.FromResult(found);
        }

        /// <summary>
        /// Retrieve the <see cref="IJobDetail" /> for the given
        /// <see cref="IJob" />.
        /// </summary>
        /// <returns>
        /// The desired <see cref="IJob" />, or null if there is no match.
        /// </returns>
        public virtual Task<IJobDetail> RetrieveJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RetrieveJobInternal(jobKey));
        }

        private IJobDetail RetrieveJobInternal(JobKey jobKey)
        {
            jobsByKey.TryGetValue(jobKey, out var jw);
            var job = jw?.JobDetail.Clone();
            return job;
        }

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <returns>
        /// The desired <see cref="ITrigger" />, or null if there is no match.
        /// </returns>
        public virtual Task<IOperableTrigger> RetrieveTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            triggersByKey.TryGetValue(triggerKey, out var tw);
            var trigger = (IOperableTrigger) tw?.Trigger.Clone();
            return Task.FromResult(trigger);
        }

        /// <summary>
        /// Determine whether a <see cref="ICalendar" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="calName">the identifier to check for</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>true if a calendar exists with the given identifier</returns>
        public Task<bool> CalendarExists(
            string calName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(calendarsByName.ContainsKey(calName));
        }

        /// <summary>
        /// Determine whether a <see cref="IJob"/> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <param name="jobKey">the identifier to check for</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>true if a Job exists with the given identifier</returns>
        public Task<bool> CheckExists(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(jobsByKey.ContainsKey(jobKey));
        }

        /// <summary>
        /// Determine whether a <see cref="ITrigger" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <param name="triggerKey">triggerKey the identifier to check for</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>true if a Trigger exists with the given identifier</returns>
        public Task<bool> CheckExists(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(triggersByKey.ContainsKey(triggerKey));
        }

        /// <summary>
        /// Get the current state of the identified <see cref="ITrigger" />.
        /// </summary>
        /// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Error" />
        /// <seealso cref="TriggerState.Blocked" />
        /// <seealso cref="TriggerState.None"/>
        public virtual Task<TriggerState> GetTriggerState(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            triggersByKey.TryGetValue(triggerKey, out var tw);

            if (tw == null)
            {
                return Task.FromResult(TriggerState.None);
            }
            if (tw.state == InternalTriggerState.Complete)
            {
                return Task.FromResult(TriggerState.Complete);
            }
            if (tw.state == InternalTriggerState.Paused)
            {
                return Task.FromResult(TriggerState.Paused);
            }
            if (tw.state == InternalTriggerState.PausedAndBlocked)
            {
                return Task.FromResult(TriggerState.Paused);
            }
            if (tw.state == InternalTriggerState.Blocked)
            {
                return Task.FromResult(TriggerState.Blocked);
            }
            if (tw.state == InternalTriggerState.Error)
            {
                return Task.FromResult(TriggerState.Error);
            }
            return Task.FromResult(TriggerState.Normal);
        }

        /// <summary>
        /// Store the given <see cref="ICalendar" />.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ICalendar" /> existing
        /// in the <see cref="IJobStore" /> with the same name and group
        /// should be over-written.</param>
        /// <param name="updateTriggers">If <see langword="true" />, any <see cref="ITrigger" />s existing
        /// in the <see cref="IJobStore" /> that reference an existing
        /// Calendar with the same name with have their next fire time
        /// re-computed with the new <see cref="ICalendar" />.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task StoreCalendar(
            string name,
            ICalendar calendar,
            bool replaceExisting,
            bool updateTriggers,
            CancellationToken cancellationToken = default)
        {
            calendar = calendar.Clone();

            lock (lockObject)
            {
                calendarsByName.TryGetValue(name, out var obj);

                if (obj != null && replaceExisting == false)
                {
                    throw new ObjectAlreadyExistsException($"Calendar with name '{name}' already exists.");
                }
                if (obj != null)
                {
                    calendarsByName.TryRemove(name, out _);
                }

                calendarsByName[name] = calendar;

                if (obj != null && updateTriggers)
                {
                    IEnumerable<TriggerWrapper> trigs = GetTriggerWrappersForCalendar(name);
                    foreach (TriggerWrapper tw in trigs)
                    {
                        IOperableTrigger trig = tw.Trigger;
                        bool removed = timeTriggers.Remove(tw);

                        trig.UpdateWithNewCalendar(calendar, MisfireThreshold);

                        if (removed)
                        {
                            timeTriggers.Add(tw);
                        }
                    }
                }
            }

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Remove (delete) the <see cref="ICalendar" /> with the
        /// given name.
        /// <para>
        /// If removal of the <see cref="ICalendar" /> would result in
        /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
        /// <see cref="JobPersistenceException" /> will be thrown.</para>
        /// </summary>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
        /// was found and removed from the store.
        /// </returns>
        public virtual Task<bool> RemoveCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RemoveCalendarInternal(calName));
        }

        private bool RemoveCalendarInternal(string calName)
        {
            lock (lockObject)
            {
                int numRefs = 0;
                foreach (TriggerWrapper triggerWrapper in triggersByKey.Values)
                {
                    IOperableTrigger trigg = triggerWrapper.Trigger;
                    if (trigg.CalendarName != null && trigg.CalendarName.Equals(calName))
                    {
                        numRefs++;
                    }
                }
                if (numRefs > 0)
                {
                    throw new JobPersistenceException("Calender cannot be removed if it referenced by a Trigger!");
                }

                return calendarsByName.TryRemove(calName, out _);
            }
        }

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>
        /// The desired <see cref="ICalendar" />, or null if there is no match.
        /// </returns>
        public virtual Task<ICalendar> RetrieveCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            calendarsByName.TryGetValue(calName, out var calendar);
            calendar = calendar?.Clone();
            return Task.FromResult(calendar);
        }

        /// <summary>
        /// Get the number of <see cref="IJobDetail" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public virtual Task<int> GetNumberOfJobs(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(jobsByKey.Count);
        }

        /// <summary>
        /// Get the number of <see cref="ITrigger" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public virtual Task<int> GetNumberOfTriggers(CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                return Task.FromResult(triggersByKey.Count);
            }
        }

        /// <summary>
        /// Get the number of <see cref="ICalendar" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        public virtual Task<int> GetNumberOfCalendars(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(calendarsByName.Count);
        }

        /// <summary>
        /// Get the names of all of the <see cref="IJob" /> s that
        /// match the given group matcher.
        /// </summary>
        public virtual Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetJobKeysInternal(matcher));
        }

        private IReadOnlyCollection<JobKey> GetJobKeysInternal(GroupMatcher<JobKey> matcher)
        {
            lock (lockObject)
            {
                ReadOnlyCompatibleHashSet<JobKey> outList = null;
                StringOperator op = matcher.CompareWithOperator;
                string compareToValue = matcher.CompareToValue;

                if (Equals(op, StringOperator.Equality))
                {
                    jobsByGroup.TryGetValue(compareToValue, out var grpMap);
                    if (grpMap != null)
                    {
                        outList = new ReadOnlyCompatibleHashSet<JobKey>();

                        foreach (JobWrapper jw in grpMap.Values)
                        {
                            if (jw != null)
                            {
                                outList.Add(jw.JobDetail.Key);
                            }
                        }
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, ConcurrentDictionary<JobKey, JobWrapper>> entry in jobsByGroup)
                    {
                        if (op.Evaluate(entry.Key, compareToValue) && entry.Value != null)
                        {
                            if (outList == null)
                            {
                                outList = new ReadOnlyCompatibleHashSet<JobKey>();
                            }
                            foreach (JobWrapper jobWrapper in entry.Value.Values)
                            {
                                if (jobWrapper != null)
                                {
                                    outList.Add(jobWrapper.JobDetail.Key);
                                }
                            }
                        }
                    }
                }
                return outList ?? new ReadOnlyCompatibleHashSet<JobKey>();
            }
        }

        /// <summary>
        /// Get the names of all of the <see cref="ICalendar" /> s
        /// in the <see cref="IJobStore" />.
        /// <para>
        /// If there are no ICalendars in the given group name, the result should be
        /// a zero-length array (not <see langword="null" />).
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetCalendarNames(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>(new List<string>(calendarsByName.Keys));
        }

        /// <summary>
        /// Get the names of all of the <see cref="ITrigger" /> s
        /// that have the given group name.
        /// </summary>
        public virtual Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetTriggerKeysInternal(matcher));
        }

        private IReadOnlyCollection<TriggerKey> GetTriggerKeysInternal(GroupMatcher<TriggerKey> matcher)
        {
            lock (lockObject)
            {
                ReadOnlyCompatibleHashSet<TriggerKey> outList = null;
                StringOperator op = matcher.CompareWithOperator;
                string compareToValue = matcher.CompareToValue;

                if (Equals(op, StringOperator.Equality))
                {
                    triggersByGroup.TryGetValue(compareToValue, out var grpMap);
                    if (grpMap != null)
                    {
                        outList = new ReadOnlyCompatibleHashSet<TriggerKey>();

                        foreach (TriggerWrapper tw in grpMap.Values)
                        {
                            if (tw != null)
                            {
                                outList.Add(tw.Trigger.Key);
                            }
                        }
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, ConcurrentDictionary<TriggerKey, TriggerWrapper>> entry in triggersByGroup)
                    {
                        if (op.Evaluate(entry.Key, compareToValue) && entry.Value != null)
                        {
                            if (outList == null)
                            {
                                outList = new ReadOnlyCompatibleHashSet<TriggerKey>();
                            }
                            foreach (TriggerWrapper triggerWrapper in entry.Value.Values)
                            {
                                if (triggerWrapper != null)
                                {
                                    outList.Add(triggerWrapper.Trigger.Key);
                                }
                            }
                        }
                    }
                }
                return outList ?? new ReadOnlyCompatibleHashSet<TriggerKey>();
            }
        }

        /// <summary>
        /// Get the names of all of the <see cref="IJob" />
        /// groups.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetJobGroupNames(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>(new List<string>(jobsByGroup.Keys));
        }

        /// <summary>
        /// Get the names of all of the <see cref="ITrigger" /> groups.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<string>>(new List<string>(triggersByGroup.Keys));
        }

        /// <summary>
        /// Get all of the Triggers that are associated to the given Job.
        /// <para>
        /// If there are no matches, a zero-length array should be returned.
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GetTriggersForJobInternal(jobKey));
        }

        private IReadOnlyCollection<IOperableTrigger> GetTriggersForJobInternal(JobKey jobKey)
        {
            lock (lockObject)
            {
                if (triggersByJob.TryGetValue(jobKey, out var jobList))
                {
                    var trigList = new List<IOperableTrigger>(jobList.Count);
                    foreach (var tw in jobList)
                    {
                        trigList.Add((IOperableTrigger) tw.Trigger.Clone());
                    }
                    return trigList;
                }
            }

            return new List<IOperableTrigger>();
        }

        /// <summary>
        /// Gets the trigger wrappers for job.
        /// </summary>
        /// <returns></returns>
        protected virtual IEnumerable<TriggerWrapper> GetTriggerWrappersForJob(
            JobKey jobKey)
        {
            lock (lockObject)
            {
                if (triggersByJob.TryGetValue(jobKey, out var jobList))
                {
                    return new List<TriggerWrapper>(jobList);
                }
            }

            return new List<TriggerWrapper>();
        }

        /// <summary>
        /// Gets the trigger wrappers for calendar.
        /// </summary>
        /// <param name="calName">Name of the cal.</param>
        /// <returns></returns>
        protected virtual IEnumerable<TriggerWrapper> GetTriggerWrappersForCalendar(string calName)
        {
            lock (lockObject)
            {
                foreach (var tw in triggersByKey.Values)
                {
                    string tcalName = tw.Trigger.CalendarName;
                    if (tcalName != null && tcalName.Equals(calName))
                    {
                        yield return tw;
                    }
                }
            }
        }

        /// <summary>
        /// Pause the <see cref="ITrigger" /> with the given name.
        /// </summary>
        public virtual Task PauseTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            PauseTriggerInternal(triggerKey);
            return TaskUtil.CompletedTask;
        }

        private void PauseTriggerInternal(TriggerKey triggerKey)
        {
            lock (lockObject)
            {
                // does the trigger exist?
                if (!triggersByKey.TryGetValue(triggerKey, out var tw) || tw.Trigger == null)
                {
                    return;
                }
                // if the trigger is "complete" pausing it does not make sense...
                if (tw.state == InternalTriggerState.Complete)
                {
                    return;
                }

                if (tw.state == InternalTriggerState.Blocked)
                {
                    tw.state = InternalTriggerState.PausedAndBlocked;
                }
                else
                {
                    tw.state = InternalTriggerState.Paused;
                }
                timeTriggers.Remove(tw);
            }
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// <para>
        /// The JobStore should "remember" that the group is paused, and impose the
        /// pause on any new triggers that are added to the group while the group is
        /// paused.
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> PauseTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PauseTriggersInternal(matcher));
        }

        private IReadOnlyCollection<string> PauseTriggersInternal(GroupMatcher<TriggerKey> matcher)
        {
            lock (lockObject)
            {
                var pausedGroups = new ReadOnlyCompatibleHashSet<string>();

                StringOperator op = matcher.CompareWithOperator;
                if (Equals(op, StringOperator.Equality))
                {
                    if (pausedTriggerGroups.Add(matcher.CompareToValue))
                    {
                        pausedGroups.Add(matcher.CompareToValue);
                    }
                }
                else
                {
                    foreach (string group in triggersByGroup.Keys)
                    {
                        if (op.Evaluate(group, matcher.CompareToValue))
                        {
                            if (pausedTriggerGroups.Add(matcher.CompareToValue))
                            {
                                pausedGroups.Add(group);
                            }
                        }
                    }
                }

                foreach (string pausedGroup in pausedGroups)
                {
                    var keys = GetTriggerKeysInternal(GroupMatcher<TriggerKey>.GroupEquals(pausedGroup));

                    foreach (TriggerKey key in keys)
                    {
                        PauseTriggerInternal(key);
                    }
                }

                return pausedGroups;
            }
        }

        /// <summary>
        /// Pause the <see cref="IJobDetail" /> with the given
        /// name - by pausing all of its current <see cref="ITrigger" />s.
        /// </summary>
        public virtual Task PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                var triggersForJob = GetTriggersForJobInternal(jobKey);
                foreach (IOperableTrigger trigger in triggersForJob)
                {
                    PauseTriggerInternal(trigger.Key);
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Pause all of the <see cref="IJobDetail" />s in the
        /// given group - by pausing all of their <see cref="ITrigger" />s.
        /// <para>
        /// The JobStore should "remember" that the group is paused, and impose the
        /// pause on any new jobs that are added to the group while the group is
        /// paused.
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> PauseJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                List<string> pausedGroups = new List<string>();
                StringOperator op = matcher.CompareWithOperator;
                if (Equals(op, StringOperator.Equality))
                {
                    if (pausedJobGroups.Add(matcher.CompareToValue))
                    {
                        pausedGroups.Add(matcher.CompareToValue);
                    }
                }
                else
                {
                    foreach (string group in jobsByGroup.Keys)
                    {
                        if (op.Evaluate(group, matcher.CompareToValue))
                        {
                            if (pausedJobGroups.Add(group))
                            {
                                pausedGroups.Add(group);
                            }
                        }
                    }
                }

                foreach (string groupName in pausedGroups)
                {
                    foreach (JobKey jobKey in GetJobKeysInternal(GroupMatcher<JobKey>.GroupEquals(groupName)))
                    {
                        var triggers = GetTriggersForJobInternal(jobKey);
                        foreach (IOperableTrigger trigger in triggers)
                        {
                            PauseTriggerInternal(trigger.Key);
                        }
                    }
                }
                return Task.FromResult<IReadOnlyCollection<string>>(pausedGroups);
            }
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="ITrigger" /> with the given key.
        /// </summary>
        /// <remarks>
        /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </remarks>
        public virtual Task ResumeTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ResumeTriggerInternal(triggerKey);
            return TaskUtil.CompletedTask;
        }

        private void ResumeTriggerInternal(TriggerKey triggerKey)
        {
            lock (lockObject)
            {
                // does the trigger exist?
                if (!triggersByKey.TryGetValue(triggerKey, out var tw) || tw.Trigger == null)
                {
                    return;
                }

                IOperableTrigger trig = tw.Trigger;

                // if the trigger is not paused resuming it does not make sense...
                if (tw.state != InternalTriggerState.Paused &&
                    tw.state != InternalTriggerState.PausedAndBlocked)
                {
                    return;
                }

                if (blockedJobs.Contains(trig.JobKey))
                {
                    tw.state = InternalTriggerState.Blocked;
                }
                else
                {
                    tw.state = InternalTriggerState.Waiting;
                }

                ApplyMisfire(tw);

                if (tw.state == InternalTriggerState.Waiting)
                {
                    timeTriggers.Add(tw);
                }
            }
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="ITrigger" />s in the
        /// given group.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> ResumeTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ResumeTriggersInternal(matcher));
        }

        private IReadOnlyCollection<string> ResumeTriggersInternal(GroupMatcher<TriggerKey> matcher)
        {
            lock (lockObject)
            {
                var groups = new ReadOnlyCompatibleHashSet<string>();
                var keys = GetTriggerKeysInternal(matcher);

                foreach (TriggerKey triggerKey in keys)
                {
                    groups.Add(triggerKey.Group);
                    if (triggersByKey.TryGetValue(triggerKey, out var tw))
                    {
                        string jobGroup = tw.JobKey.Group;
                        if (pausedJobGroups.Contains(jobGroup))
                        {
                            continue;
                        }
                    }
                    ResumeTriggerInternal(triggerKey);
                }
                // Find all matching paused trigger groups, and then remove them.
                StringOperator op = matcher.CompareWithOperator;
                var pausedGroups = new List<string>();
                var matcherGroup = matcher.CompareToValue;
                if (op.Equals(StringOperator.Equality))
                {
                    if (pausedTriggerGroups.Contains(matcherGroup))
                    {
                        pausedGroups.Add(matcher.CompareToValue);
                    }
                    else
                    {
                        foreach (string group in pausedTriggerGroups)
                        {
                            if (op.Evaluate(group, matcherGroup))
                            {
                                pausedGroups.Add(group);
                            }
                        }
                    }
                    foreach (string pausedGroup in pausedGroups)
                    {
                        pausedTriggerGroups.Remove(pausedGroup);
                    }
                }
                return groups;
            }
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="IJobDetail" /> with
        /// the given name.
        /// <para>
        /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
        /// or more fire-times, then the <see cref="ITrigger" />'s misfire
        /// instruction will be applied.
        /// </para>
        /// </summary>
        public virtual Task ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                var triggersForJob = GetTriggersForJobInternal(jobKey);
                foreach (IOperableTrigger trigger in triggersForJob)
                {
                    ResumeTriggerInternal(trigger.Key);
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="IJobDetail" />s
        /// in the given group.
        /// <para>
        /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
        /// missed one or more fire-times, then the <see cref="ITrigger" />'s
        /// misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> ResumeJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                var resumedGroups = new ReadOnlyCompatibleHashSet<string>();
                var keys = GetJobKeysInternal(matcher);

                foreach (string pausedJobGroup in pausedJobGroups)
                {
                    if (matcher.CompareWithOperator.Evaluate(pausedJobGroup, matcher.CompareToValue))
                    {
                        resumedGroups.Add(pausedJobGroup);
                    }
                }

                foreach (string resumedGroup in resumedGroups)
                {
                    pausedJobGroups.Remove(resumedGroup);
                }

                foreach (JobKey key in keys)
                {
                    var triggers = GetTriggersForJobInternal(key);
                    foreach (IOperableTrigger trigger in triggers)
                    {
                        ResumeTriggerInternal(trigger.Key);
                    }
                }
                return Task.FromResult<IReadOnlyCollection<string>>(resumedGroups);
            }
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggers" />
        /// on every group.
        /// <para>
        /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="ResumeAll(CancellationToken)" />
        public virtual Task PauseAll(CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                foreach (string groupName in triggersByGroup.Keys)
                {
                    PauseTriggersInternal(GroupMatcher<TriggerKey>.GroupEquals(groupName));
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers" />
        /// on every trigger group and setting all job groups unpaused />.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="PauseAll(CancellationToken)" />
        public virtual Task ResumeAll(CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                // TODO need a match all here!
                pausedJobGroups.Clear();

                foreach (string groupName in triggersByGroup.Keys)
                {
                    ResumeTriggersInternal(GroupMatcher<TriggerKey>.GroupEquals(groupName));
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Applies the misfire.
        /// </summary>
        /// <param name="tw">The trigger wrapper.</param>
        /// <returns></returns>
        protected virtual bool ApplyMisfire(TriggerWrapper tw)
        {
            DateTimeOffset misfireTime = SystemTime.UtcNow();
            if (MisfireThreshold > TimeSpan.Zero)
            {
                misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
            }

            DateTimeOffset? tnft = tw.Trigger.GetNextFireTimeUtc();
            if (!tnft.HasValue || tnft.Value > misfireTime
                || tw.Trigger.MisfireInstruction == MisfireInstruction.IgnoreMisfirePolicy)
            {
                return false;
            }

            ICalendar cal = null;
            if (tw.Trigger.CalendarName != null)
            {
                calendarsByName.TryGetValue(tw.Trigger.CalendarName, out cal);
            }

            signaler.NotifyTriggerListenersMisfired((IOperableTrigger) tw.Trigger.Clone()).ConfigureAwait(false).GetAwaiter().GetResult();
            ;

            tw.Trigger.UpdateAfterMisfire(cal);

            if (!tw.Trigger.GetNextFireTimeUtc().HasValue)
            {
                tw.state = InternalTriggerState.Complete;
                signaler.NotifySchedulerListenersFinalized(tw.Trigger).ConfigureAwait(false).GetAwaiter().GetResult();
                ;
                lock (lockObject)
                {
                    timeTriggers.Remove(tw);
                }
            }
            else if (tnft.Equals(tw.Trigger.GetNextFireTimeUtc()))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get a handle to the next trigger to be fired, and mark it as 'reserved'
        /// by the calling scheduler.
        /// </summary>
        /// <seealso cref="ITrigger" />
        public virtual Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(
            DateTimeOffset noLaterThan,
            int maxCount,
            TimeSpan timeWindow,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                var result = new List<IOperableTrigger>();
                var acquiredJobKeysForNoConcurrentExec = new HashSet<JobKey>();
                var excludedTriggers = new HashSet<TriggerWrapper>();
                DateTimeOffset batchEnd = noLaterThan;

                // return empty list if store has no triggers.
                if (timeTriggers.Count == 0)
                {
                    return Task.FromResult<IReadOnlyCollection<IOperableTrigger>>(result);
                }

                while (true)
                {
                    var tw = timeTriggers.FirstOrDefault();
                    if (tw == null)
                    {
                        break;
                    }
                    if (!timeTriggers.Remove(tw))
                    {
                        break;
                    }

                    if (tw.Trigger.GetNextFireTimeUtc() == null)
                    {
                        continue;
                    }

                    if (ApplyMisfire(tw))
                    {
                        if (tw.Trigger.GetNextFireTimeUtc() != null)
                        {
                            timeTriggers.Add(tw);
                        }
                        continue;
                    }

                    if (tw.Trigger.GetNextFireTimeUtc() > batchEnd)
                    {
                        timeTriggers.Add(tw);
                        break;
                    }

                    // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                    // put it back into the timeTriggers set and continue to search for next trigger.
                    JobKey jobKey = tw.Trigger.JobKey;
                    IJobDetail job = jobsByKey[tw.Trigger.JobKey].JobDetail;
                    if (job.ConcurrentExecutionDisallowed)
                    {
                        if (acquiredJobKeysForNoConcurrentExec.Contains(jobKey))
                        {
                            excludedTriggers.Add(tw);
                            continue; // go to next trigger in store.
                        }
                        acquiredJobKeysForNoConcurrentExec.Add(jobKey);
                    }

                    tw.state = InternalTriggerState.Acquired;
                    tw.Trigger.FireInstanceId = GetFiredTriggerRecordId();
                    IOperableTrigger trig = (IOperableTrigger) tw.Trigger.Clone();

                    if (result.Count == 0)
                    {
                        var now = SystemTime.UtcNow();
                        var nextFireTime = tw.Trigger.GetNextFireTimeUtc().GetValueOrDefault(DateTimeOffset.MinValue);
                        var max = now > nextFireTime ? now : nextFireTime;

                        batchEnd = max + timeWindow;
                    }

                    result.Add(trig);

                    if (result.Count == maxCount)
                    {
                        break;
                    }
                }

                // If we did excluded triggers to prevent ACQUIRE state due to DisallowConcurrentExecution, we need to add them back to store.
                if (excludedTriggers.Count > 0)
                {
                    foreach (var excludedTrigger in excludedTriggers)
                    {
                        timeTriggers.Add(excludedTrigger);
                    }
                }
                return Task.FromResult<IReadOnlyCollection<IOperableTrigger>>(result);
            }
        }

        /// <summary>
        /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
        /// fire the given <see cref="ITrigger" />, that it had previously acquired
        /// (reserved).
        /// </summary>
        public virtual Task ReleaseAcquiredTrigger(
            IOperableTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                if (triggersByKey.TryGetValue(trigger.Key, out var tw) && tw.state == InternalTriggerState.Acquired)
                {
                    tw.state = InternalTriggerState.Waiting;
                    timeTriggers.Add(tw);
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Inform the <see cref="IJobStore" /> that the scheduler is now firing the
        /// given <see cref="ITrigger" /> (executing its associated <see cref="IJob" />),
        /// that it had previously acquired (reserved).
        /// </summary>
        public virtual Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(
            IReadOnlyCollection<IOperableTrigger> triggers,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                List<TriggerFiredResult> results = new List<TriggerFiredResult>();

                foreach (IOperableTrigger trigger in triggers)
                {
                    // was the trigger deleted since being acquired?
                    if (!triggersByKey.TryGetValue(trigger.Key, out var tw) || tw.Trigger == null)
                    {
                        continue;
                    }
                    // was the trigger completed, paused, blocked, etc. since being acquired?
                    if (tw.state != InternalTriggerState.Acquired)
                    {
                        continue;
                    }

                    ICalendar cal = null;
                    if (tw.Trigger.CalendarName != null)
                    {
                        calendarsByName.TryGetValue(tw.Trigger.CalendarName, out cal);
                        if (cal == null)
                        {
                            continue;
                        }
                    }
                    DateTimeOffset? prevFireTime = trigger.GetPreviousFireTimeUtc();
                    // in case trigger was replaced between acquiring and firing
                    timeTriggers.Remove(tw);
                    // call triggered on our copy, and the scheduler's copy
                    tw.Trigger.Triggered(cal);
                    trigger.Triggered(cal);
                    //tw.state = TriggerWrapper.STATE_EXECUTING;
                    tw.state = InternalTriggerState.Waiting;

                    var jobDetail = RetrieveJobInternal(trigger.JobKey);
                    TriggerFiredBundle bndle = new TriggerFiredBundle(
                        jobDetail,
                        trigger,
                        cal,
                        false,
                        SystemTime.UtcNow(),
                        trigger.GetPreviousFireTimeUtc(),
                        prevFireTime,
                        trigger.GetNextFireTimeUtc());

                    IJobDetail job = bndle.JobDetail;

                    if (job.ConcurrentExecutionDisallowed)
                    {
                        IEnumerable<TriggerWrapper> trigs = GetTriggerWrappersForJob(job.Key);
                        foreach (TriggerWrapper ttw in trigs)
                        {
                            if (ttw.state == InternalTriggerState.Waiting)
                            {
                                ttw.state = InternalTriggerState.Blocked;
                            }
                            if (ttw.state == InternalTriggerState.Paused)
                            {
                                ttw.state = InternalTriggerState.PausedAndBlocked;
                            }
                            timeTriggers.Remove(ttw);
                        }
                        blockedJobs.Add(job.Key);
                    }
                    else if (tw.Trigger.GetNextFireTimeUtc() != null)
                    {
                        lock (lockObject)
                        {
                            timeTriggers.Add(tw);
                        }
                    }

                    results.Add(new TriggerFiredResult(bndle));
                }
                return Task.FromResult<IReadOnlyCollection<TriggerFiredResult>>(results);
            }
        }

        /// <summary>
        /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
        /// firing of the given <see cref="ITrigger" /> (and the execution its
        /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
        /// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
        /// is stateful.
        /// </summary>
        public virtual Task TriggeredJobComplete(
            IOperableTrigger trigger,
            IJobDetail jobDetail,
            SchedulerInstruction triggerInstCode,
            CancellationToken cancellationToken = default)
        {
            lock (lockObject)
            {
                triggersByKey.TryGetValue(trigger.Key, out var tw);

                // It's possible that the job is null if:
                //   1- it was deleted during execution
                //   2- RAMJobStore is being used only for volatile jobs / triggers
                //      from the JDBC job store

                if (jobsByKey.TryGetValue(jobDetail.Key, out var jw))
                {
                    IJobDetail jd = jw.JobDetail;

                    if (jobDetail.PersistJobDataAfterExecution)
                    {
                        JobDataMap newData = jobDetail.JobDataMap;
                        if (newData != null)
                        {
                            newData = (JobDataMap) newData.Clone();
                            newData.ClearDirtyFlag();
                        }
                        jd = jd.GetJobBuilder().SetJobData(newData).Build();
                        jw.JobDetail = jd;
                    }
                    if (jd.ConcurrentExecutionDisallowed)
                    {
                        blockedJobs.Remove(jd.Key);
                        IEnumerable<TriggerWrapper> trigs = GetTriggerWrappersForJob(jd.Key);
                        foreach (TriggerWrapper ttw in trigs)
                        {
                            if (ttw.state == InternalTriggerState.Blocked)
                            {
                                ttw.state = InternalTriggerState.Waiting;
                                timeTriggers.Add(ttw);
                            }
                            if (ttw.state == InternalTriggerState.PausedAndBlocked)
                            {
                                ttw.state = InternalTriggerState.Paused;
                            }
                        }

                        signaler.SignalSchedulingChange(null, cancellationToken);
                    }
                }
                else
                {
                    // even if it was deleted, there may be cleanup to do
                    blockedJobs.Remove(jobDetail.Key);
                }

                // check for trigger deleted during execution...
                if (tw != null)
                {
                    if (triggerInstCode == SchedulerInstruction.DeleteTrigger)
                    {
                        Log.Debug("Deleting trigger");
                        DateTimeOffset? d = trigger.GetNextFireTimeUtc();
                        if (!d.HasValue)
                        {
                            // double check for possible reschedule within job
                            // execution, which would cancel the need to delete...
                            d = tw.Trigger.GetNextFireTimeUtc();
                            if (!d.HasValue)
                            {
                                RemoveTriggerInternal(trigger.Key);
                            }
                            else
                            {
                                Log.Debug("Deleting cancelled - trigger still active");
                            }
                        }
                        else
                        {
                            RemoveTriggerInternal(trigger.Key);
                            signaler.SignalSchedulingChange(null, cancellationToken);
                        }
                    }
                    else if (triggerInstCode == SchedulerInstruction.SetTriggerComplete)
                    {
                        tw.state = InternalTriggerState.Complete;
                        timeTriggers.Remove(tw);
                        signaler.SignalSchedulingChange(null, cancellationToken);
                    }
                    else if (triggerInstCode == SchedulerInstruction.SetTriggerError)
                    {
                        Log.Info($"Trigger {trigger.Key} set to ERROR state.");
                        tw.state = InternalTriggerState.Error;
                        signaler.SignalSchedulingChange(null, cancellationToken);
                    }
                    else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersError)
                    {
                        Log.Info($"All triggers of Job {trigger.JobKey} set to ERROR state.");
                        SetAllTriggersOfJobToState(trigger.JobKey, InternalTriggerState.Error);
                        signaler.SignalSchedulingChange(null, cancellationToken);
                    }
                    else if (triggerInstCode == SchedulerInstruction.SetAllJobTriggersComplete)
                    {
                        SetAllTriggersOfJobToState(trigger.JobKey, InternalTriggerState.Complete);
                        signaler.SignalSchedulingChange(null, cancellationToken);
                    }
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Inform the <see cref="IJobStore" /> of the Scheduler instance's Id,
        /// prior to initialize being invoked.
        /// </summary>
        public virtual string InstanceId
        {
            set { }
        }

        /// <summary>
        /// Inform the <see cref="IJobStore" /> of the Scheduler instance's name,
        /// prior to initialize being invoked.
        /// </summary>
        public virtual string InstanceName
        {
            set { }
        }

        public int ThreadPoolSize
        {
            set { }
        }

        public long EstimatedTimeToReleaseAndAcquireTrigger => 5;

        public bool Clustered => false;

        /// <summary>
        /// Sets the state of all triggers of job to specified state.
        /// </summary>
        protected virtual void SetAllTriggersOfJobToState(JobKey jobKey, InternalTriggerState state)
        {
            foreach (TriggerWrapper tw in GetTriggerWrappersForJob(jobKey))
            {
                tw.state = state;
                if (state != InternalTriggerState.Waiting)
                {
                    timeTriggers.Remove(tw);
                }
            }
        }

        /// <summary>
        /// Peeks the triggers.
        /// </summary>
        /// <returns></returns>
        protected internal virtual Task<string> PeekTriggers()
        {
            StringBuilder str = new StringBuilder();

            lock (lockObject)
            {
                foreach (TriggerWrapper tw in triggersByKey.Values)
                {
                    str.Append(tw.Trigger.Key.Name);
                    str.Append("/");
                }

                str.Append(" | ");

                foreach (TriggerWrapper tw in timeTriggers)
                {
                    str.Append(tw.Trigger.Key.Name);
                    str.Append("->");
                }
            }

            return Task.FromResult(str.ToString());
        }

        /// <seealso cref="IJobStore.GetPausedTriggerGroups" />
        public virtual Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            CancellationToken cancellationToken = default)
        {
            var data = new ReadOnlyCompatibleHashSet<string>(pausedTriggerGroups);
            return Task.FromResult<IReadOnlyCollection<string>>(data);
        }
    }
}