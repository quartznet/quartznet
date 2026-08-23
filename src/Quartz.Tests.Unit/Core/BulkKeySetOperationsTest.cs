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

#nullable enable

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Listeners;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What a key-set pause, resume, error-state reset, delete or unschedule tells the outside world.
/// </summary>
/// <remarks>
/// The answers themselves belong to the job store contract; what belongs here is the part only the
/// scheduler can get wrong. A key set is neither one key nor one group, so the notification shape
/// had to be chosen: the events stay per key, because <c>TriggersPaused(null)</c> means every group
/// and would read to a monitoring listener as a total outage. The scheduling change, in contrast, is
/// raised once for the call — the scheduler thread treats it as a level, not an edge, so one signal
/// covers the whole set. That is why the scheduler must reach the store once, and never loop.
/// </remarks>
[NonParallelizable]
public class BulkKeySetOperationsTest
{
    private CountingJobStore store = null!;
    private RecordingSchedulerListener listener = null!;
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task BuildScheduler()
    {
        listener = new RecordingSchedulerListener();

        IScheduler built = await QuartzSchedulerBuilder.Create()
            .UseJobStore(provider =>
            {
                store = new CountingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                return store;
            })
            .BuildScheduler();

        built.ListenerManager.AddSchedulerListener(listener);
        scheduler = built;
    }

    [TearDown]
    public async Task ShutDownScheduler()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
        scheduler = null!;
    }

    [Test]
    public async Task PausingASetOfTriggersRaisesOneEventPerAppliedKeyAndReachesTheStoreOnce()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey missing = new TriggerKey("missing", "nowhere");

        List<TriggerKey> paused = await scheduler.PauseTriggers([first, missing, second]);

        paused.Should().Equal([first, second]);

        listener.PausedTriggers.Should().Equal([first, second],
            "the pause is reported one key at a time, and only for the keys it applied to");
        listener.PausedTriggerGroups.Should().BeEmpty(
            "a key set is not a group — a group event with a null group means every group, which reads "
            + "as a total outage to anything watching");

        store.BulkPauseTriggerCalls.Should().Be(1,
            "one call into the store is what makes one scheduling signal possible");
        store.SinglePauseTriggerCalls.Should().Be(0, "a loop over the single-key member would signal per key");
    }

    [Test]
    public async Task ResumingASetOfTriggersRaisesOneEventPerAppliedKeyAndReachesTheStoreOnce()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey neverPaused = await Schedule("never-paused");

        await scheduler.PauseTriggers([first, second]);
        listener.Clear();

        List<TriggerKey> resumed = await scheduler.ResumeTriggers([first, neverPaused, second]);

        resumed.Should().Equal([first, second], "the trigger that was not paused had nothing to resume");
        listener.ResumedTriggers.Should().Equal([first, second]);
        listener.ResumedTriggerGroups.Should().BeEmpty();

        store.BulkResumeTriggerCalls.Should().Be(1);
        store.SingleResumeTriggerCalls.Should().Be(0);
    }

    [Test]
    public async Task PausingAndResumingASetOfJobsRaisesOneEventPerAppliedKey()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        JobKey firstJob = new JobKey("first", "jobs");
        JobKey secondJob = new JobKey("second", "jobs");
        JobKey missing = new JobKey("missing", "nowhere");

        List<JobKey> paused = await scheduler.PauseJobs([firstJob, missing, secondJob]);

        paused.Should().Equal([firstJob, secondJob]);
        listener.PausedJobs.Should().Equal([firstJob, secondJob]);
        listener.PausedJobGroups.Should().BeEmpty();
        store.BulkPauseJobCalls.Should().Be(1);

        (await scheduler.GetTriggerState(first)).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerState(second)).Should().Be(TriggerState.Paused);

        listener.Clear();

        List<JobKey> resumed = await scheduler.ResumeJobs([firstJob, missing, secondJob]);

        resumed.Should().Equal([firstJob, secondJob]);
        listener.ResumedJobs.Should().Equal([firstJob, secondJob]);
        listener.ResumedJobGroups.Should().BeEmpty();
        store.BulkResumeJobCalls.Should().Be(1);
    }

    [Test]
    public async Task DeletingASetOfJobsAnswersWithTheKeysItDeletedAndRaisesOneEventEach()
    {
        // Durable, so that removing the job's trigger does not orphan it: the store announces a job it
        // deletes on its own account, and this test is about what the scheduler announces.
        await Schedule("first", durable: true);
        await Schedule("second", durable: true);
        JobKey firstJob = new JobKey("first", "jobs");
        JobKey secondJob = new JobKey("second", "jobs");
        JobKey missing = new JobKey("missing", "nowhere");

        List<JobKey> deleted = await scheduler.DeleteJobs([firstJob, missing, secondJob]);

        deleted.Should().Equal([firstJob, secondJob],
            "two of the three keys named a job, and the answer says which two — the old bool could "
            + "only say that not all three did, which a caller could not tell from nothing happening");

        listener.DeletedJobs.Should().Equal([firstJob, secondJob],
            "a key that named no job was never deleted, so nothing is announced for it");

        store.BulkDeleteJobCalls.Should().Be(1, "one call into the store is what makes one scheduling signal possible");
        store.SingleDeleteJobCalls.Should().Be(0, "a loop over the single-key member would signal per key");

        (await scheduler.Exists(firstJob)).Should().BeFalse();
        (await scheduler.Exists(secondJob)).Should().BeFalse();
    }

    [Test]
    public async Task UnschedulingASetOfTriggersAnswersWithTheKeysItRemovedAndRaisesOneEventEach()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey missing = new TriggerKey("missing", "nowhere");

        List<TriggerKey> unscheduled = await scheduler.UnscheduleJobs([first, missing, second]);

        unscheduled.Should().Equal([first, second]);
        listener.UnscheduledTriggers.Should().Equal([first, second]);

        store.BulkDeleteTriggerCalls.Should().Be(1);
        store.SingleDeleteTriggerCalls.Should().Be(0);
    }

    /// <summary>
    /// A key that named nothing used to be announced as deleted anyway.
    /// </summary>
    /// <remarks>
    /// The old implementation raised one <c>JobDeleted</c> per key <em>given</em>, so a caller who
    /// passed a mistyped key told every scheduler listener that a job by that name had been deleted.
    /// The key-set pause family never did that, and neither does the single-key <c>DeleteJob</c>.
    /// </remarks>
    [Test]
    public async Task AKeySetThatDeletedNothingAnnouncesNothing()
    {
        JobKey missing = new JobKey("missing", "nowhere");
        TriggerKey missingTrigger = new TriggerKey("missing", "nowhere");

        (await scheduler.DeleteJobs([missing])).Should().BeEmpty();
        (await scheduler.UnscheduleJobs([missingTrigger])).Should().BeEmpty();

        listener.DeletedJobs.Should().BeEmpty(
            "a job that was never there was never deleted, and saying otherwise misinforms every listener");
        listener.UnscheduledTriggers.Should().BeEmpty();
    }

    [Test]
    public async Task AKeySetThatMovedNothingRaisesNothing()
    {
        TriggerKey missing = new TriggerKey("missing", "nowhere");

        List<TriggerKey> paused = await scheduler.PauseTriggers([missing]);

        paused.Should().BeEmpty();
        listener.PausedTriggers.Should().BeEmpty("a no-op must not look like a state change to a listener");
    }

    [Test]
    public async Task AnEmptyKeySetNeverReachesTheStore()
    {
        (await scheduler.PauseTriggers([])).Should().BeEmpty();
        (await scheduler.ResumeTriggers([])).Should().BeEmpty();
        (await scheduler.PauseJobs([])).Should().BeEmpty();
        (await scheduler.ResumeJobs([])).Should().BeEmpty();
        (await scheduler.ResetTriggersFromErrorState([])).Should().BeEmpty();
        (await scheduler.DeleteJobs([])).Should().BeEmpty();
        (await scheduler.UnscheduleJobs([])).Should().BeEmpty();

        store.BulkPauseTriggerCalls.Should().Be(0, "nothing to do is answered without opening a connection");
        store.BulkResumeTriggerCalls.Should().Be(0);
        store.BulkPauseJobCalls.Should().Be(0);
        store.BulkResumeJobCalls.Should().Be(0);
        store.BulkResetCalls.Should().Be(0);
        store.BulkDeleteJobCalls.Should().Be(0);
        store.BulkDeleteTriggerCalls.Should().Be(0);
    }

    [Test]
    public async Task AKeySetIsRequired()
    {
        Func<Task> pausingNothing = async () => await scheduler.PauseTriggers((IReadOnlyCollection<TriggerKey>) null!);
        await pausingNothing.Should().ThrowAsync<ArgumentNullException>();

        Func<Task> deletingNothing = async () => await scheduler.DeleteJobs(null!);
        await deletingNothing.Should().ThrowAsync<ArgumentNullException>();

        Func<Task> unschedulingNothing = async () => await scheduler.UnscheduleJobs(null!);
        await unschedulingNothing.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<TriggerKey> Schedule(string name, bool durable = false)
    {
        IJobDetail job = JobBuilder.Create<NoOpBulkJob>().WithIdentity(name, "jobs").StoreDurably(durable).Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(name, "triggers")
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    public sealed class NoOpBulkJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Counts how the scheduler reaches the store: once for the set, or once per key.
    /// </summary>
    private sealed class CountingJobStore : DelegatingJobStore
    {
        public CountingJobStore(IJobStore inner) : base(inner)
        {
        }

        public int SinglePauseTriggerCalls { get; private set; }
        public int SingleResumeTriggerCalls { get; private set; }
        public int SingleDeleteJobCalls { get; private set; }
        public int SingleDeleteTriggerCalls { get; private set; }
        public int BulkPauseTriggerCalls { get; private set; }
        public int BulkResumeTriggerCalls { get; private set; }
        public int BulkPauseJobCalls { get; private set; }
        public int BulkResumeJobCalls { get; private set; }
        public int BulkResetCalls { get; private set; }
        public int BulkDeleteJobCalls { get; private set; }
        public int BulkDeleteTriggerCalls { get; private set; }

        public override ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            SingleDeleteJobCalls++;
            return base.DeleteJob(jobKey, cancellationToken);
        }

        public override ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            SingleDeleteTriggerCalls++;
            return base.DeleteTrigger(triggerKey, cancellationToken);
        }

        public override ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            BulkDeleteJobCalls++;
            return base.DeleteJobs(jobKeys, cancellationToken);
        }

        public override ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            BulkDeleteTriggerCalls++;
            return base.DeleteTriggers(triggerKeys, cancellationToken);
        }

        public override ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            SinglePauseTriggerCalls++;
            return base.PauseTrigger(triggerKey, cancellationToken);
        }

        public override ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            SingleResumeTriggerCalls++;
            return base.ResumeTrigger(triggerKey, cancellationToken);
        }

        public override ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            BulkPauseTriggerCalls++;
            return base.PauseTriggers(triggerKeys, cancellationToken);
        }

        public override ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            BulkResumeTriggerCalls++;
            return base.ResumeTriggers(triggerKeys, cancellationToken);
        }

        public override ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            BulkPauseJobCalls++;
            return base.PauseJobs(jobKeys, cancellationToken);
        }

        public override ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            BulkResumeJobCalls++;
            return base.ResumeJobs(jobKeys, cancellationToken);
        }

        public override ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            BulkResetCalls++;
            return base.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
        }
    }

    private sealed class RecordingSchedulerListener : ISchedulerListener
    {
        public List<TriggerKey> PausedTriggers { get; } = [];
        public List<TriggerKey> ResumedTriggers { get; } = [];
        public List<JobKey> PausedJobs { get; } = [];
        public List<JobKey> ResumedJobs { get; } = [];
        public List<JobKey> DeletedJobs { get; } = [];
        public List<TriggerKey> UnscheduledTriggers { get; } = [];
        public List<string?> PausedTriggerGroups { get; } = [];
        public List<string?> ResumedTriggerGroups { get; } = [];
        public List<string?> PausedJobGroups { get; } = [];
        public List<string?> ResumedJobGroups { get; } = [];

        public void Clear()
        {
            PausedTriggers.Clear();
            ResumedTriggers.Clear();
            PausedJobs.Clear();
            ResumedJobs.Clear();
            DeletedJobs.Clear();
            UnscheduledTriggers.Clear();
            PausedTriggerGroups.Clear();
            ResumedTriggerGroups.Clear();
            PausedJobGroups.Clear();
            ResumedJobGroups.Clear();
        }

        public ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            DeletedJobs.Add(jobKey);
            return default;
        }

        public ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            UnscheduledTriggers.Add(triggerKey);
            return default;
        }

        public ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            PausedTriggers.Add(triggerKey);
            return default;
        }

        public ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default)
        {
            PausedTriggerGroups.Add(triggerGroup);
            return default;
        }

        public ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            ResumedTriggers.Add(triggerKey);
            return default;
        }

        public ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default)
        {
            ResumedTriggerGroups.Add(triggerGroup);
            return default;
        }

        public ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            PausedJobs.Add(jobKey);
            return default;
        }

        public ValueTask JobsPaused(string? jobGroup, CancellationToken cancellationToken = default)
        {
            PausedJobGroups.Add(jobGroup);
            return default;
        }

        public ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            ResumedJobs.Add(jobKey);
            return default;
        }

        public ValueTask JobsResumed(string? jobGroup, CancellationToken cancellationToken = default)
        {
            ResumedJobGroups.Add(jobGroup);
            return default;
        }
    }
}
