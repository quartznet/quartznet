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

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;

namespace Quartz.Tests.Unit;

/// <summary>
/// A pause that says why, through a real scheduler: what it records, what it tells the listeners, and
/// the listener <c>PauseTriggerWhenRetriesExhausted()</c> registers.
/// </summary>
[NonParallelizable]
public sealed class PauseReasonSchedulerTest
{
    private static readonly PauseDetails maintenance = new() { Reason = "database maintenance", RequestedBy = "alice" };

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Pausing with a reason
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task EveryPauseWithAReasonIsRecordedAndAnnouncedAsThePauseItIs()
    {
        PauseRecorder recorder = new();
        await using ServiceProvider provider = BuildScheduler("with-reason", quartz =>
            quartz.AddSchedulerListener(recorder));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        TriggerKey nightly = new("nightly", "reports");
        TriggerKey hourly = new("hourly", "imports");

        (await scheduler.PauseTriggerWith(nightly, maintenance)).Should().BeTrue();
        (await scheduler.GetTriggerPause(nightly)).Should().Match<PauseInfo>(
            pause => pause.Reason == "database maintenance" && pause.RequestedBy == "alice",
            "the scheduler hands the details to its store, which records them");

        (await scheduler.PauseJobWith(new JobKey("import", "imports"), maintenance)).Should().BeTrue();
        (await scheduler.GetTriggerPause(hourly))!.Reason.Should().Be("database maintenance");

        await scheduler.ResumeAll();

        (await scheduler.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("reports"), maintenance)).Should().Equal(["reports"]);
        (await scheduler.GetTriggerGroupPause("reports"))!.RequestedBy.Should().Be("alice");

        (await scheduler.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("imports"), maintenance)).Should().Equal(["imports"]);
        (await scheduler.GetJobGroupPause("imports"))!.RequestedBy.Should().Be("alice");

        await scheduler.ResumeAll();
        await scheduler.PauseAllWith(maintenance);
        (await scheduler.GetTriggerGroupPause("imports"))!.Reason.Should().Be("database maintenance");

        recorder.Events.Should().Equal(
            [
                "trigger reports.nightly",
                "job imports.import",
                "trigger group reports",
                "job group imports",
                "all triggers"
            ],
            "a pause with a reason is the pause it names, so its listeners hear what they always heard");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task APauseThatMovedNothingIsNotAnnounced()
    {
        PauseRecorder recorder = new();
        await using ServiceProvider provider = BuildScheduler("nothing-moved", quartz =>
            quartz.AddSchedulerListener(recorder));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await scheduler.PauseTriggerWith(new TriggerKey("missing", "reports"), maintenance)).Should().BeFalse();
        (await scheduler.PauseJobWith(new JobKey("missing", "reports"), maintenance)).Should().BeFalse();

        recorder.Events.Should().BeEmpty("nothing was paused, so nothing is announced");
        await scheduler.Shutdown();
    }

    [Test]
    public async Task TheArgumentsAreChecked()
    {
        await using ServiceProvider provider = BuildScheduler("arguments");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Func<Task> withoutTriggerKey = async () => await scheduler.PauseTriggerWith(null!, maintenance);
        Func<Task> withoutKey = async () => await scheduler.PauseJobWith(null!, maintenance);
        Func<Task> withoutMatcher = async () => await scheduler.PauseTriggerGroupsWith(null!, maintenance);
        Func<Task> withoutJobMatcher = async () => await scheduler.PauseJobGroupsWith(null!, maintenance);
        Func<Task> reasonlessWithoutKey = async () => await scheduler.PauseTriggerWith(null!, null);
        Func<Task> readWithoutKey = async () => await scheduler.GetTriggerPause(null!);
        Func<Task> readWithoutGroup = async () => await scheduler.GetTriggerGroupPause(null!);
        Func<Task> readWithoutJobGroup = async () => await scheduler.GetJobGroupPause(null!);

        await withoutTriggerKey.Should().ThrowAsync<ArgumentNullException>();
        await withoutKey.Should().ThrowAsync<ArgumentNullException>();
        await withoutMatcher.Should().ThrowAsync<ArgumentNullException>();
        await withoutJobMatcher.Should().ThrowAsync<ArgumentNullException>();
        await reasonlessWithoutKey.Should().ThrowAsync<ArgumentNullException>(
            "a pause that says nothing is the reasonless member, which checks its key as it always did");
        await readWithoutKey.Should().ThrowAsync<ArgumentNullException>();
        await readWithoutGroup.Should().ThrowAsync<ArgumentNullException>();
        await readWithoutJobGroup.Should().ThrowAsync<ArgumentNullException>();

        await scheduler.Shutdown();
    }

    [Test]
    public async Task DetailsThatSayNothingAreTheReasonlessPauseAndRecordNothing()
    {
        PauseRecorder recorder = new();
        await using ServiceProvider provider = BuildScheduler("says-nothing", quartz =>
            quartz.AddSchedulerListener(recorder));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        TriggerKey nightly = new("nightly", "reports");

        (await scheduler.PauseTriggerWith(nightly, null)).Should().BeTrue();
        (await scheduler.PauseJobWith(new JobKey("import", "imports"), new PauseDetails())).Should().BeTrue();
        (await scheduler.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("reports"), null)).Should().Equal(["reports"]);
        (await scheduler.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("imports"), new PauseDetails())).Should().Equal(["imports"]);
        await scheduler.PauseAllWith(new PauseDetails { Reason = " ", RequestedBy = "" });

        (await scheduler.GetTriggerState(nightly)).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerPause(nightly)).Should().BeNull(
            "null, or details with nothing in them, is the pause every caller made before 4.3, and it records nothing");
        (await scheduler.GetTriggerGroupPause("reports")).Should().BeNull();
        (await scheduler.GetJobGroupPause("imports")).Should().BeNull();

        recorder.Events.Should().Equal(
            ["trigger reports.nightly", "job imports.import", "trigger group reports", "job group imports", "all triggers"],
            "the reasonless pause is announced as it always was");

        await scheduler.Shutdown();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Pausing a trigger whose retries ran out
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ATriggerWhoseRetriesRanOutIsPausedWithTheJobsMessage()
    {
        FailingJob.Attempts = 0;
        await using ServiceProvider provider = BuildScheduler("exhausted", quartz =>
        {
            quartz.PauseTriggerWhenRetriesExhausted();
            quartz.AddJob<FailingJob>(job => job.WithIdentity("flaky", "retries"));
            quartz.AddTrigger(trigger => trigger
                .ForJob("flaky", "retries")
                .WithIdentity("flaky", "retries")
                // An occurrence an hour, so a trigger left alone would go back to its schedule.
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .WithRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(100)))
                .StartNow());
        }, addJobs: false);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        TriggerKey key = new("flaky", "retries");
        PauseInfo? pause;

        try
        {
            await scheduler.Start();
            await WaitUntil(async () => await scheduler.GetTriggerState(key) == TriggerState.Paused,
                "the job fails on its first attempt and its one retry, so the occurrence gives up and the listener pauses it");
            pause = await scheduler.GetTriggerPause(key);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        pause.Should().NotBeNull();
        pause!.Reason.Should().Be("the upstream system is down",
            "the reason is what the job threw, not the run shell's word for having caught it");
        pause.RequestedBy.Should().Be("quartz:retries-exhausted",
            "the requester says the scheduler paused it on its own account, not an operator");
        FailingJob.Attempts.Should().Be(2, "one attempt and its one retry, and then the trigger stopped");
    }

    [Test]
    public async Task RegisteringThePauseOnExhaustedRetriesTwiceRegistersOneListener()
    {
        await using ServiceProvider provider = BuildScheduler("registered-twice", quartz =>
        {
            quartz.PauseTriggerWhenRetriesExhausted();
            quartz.PauseTriggerWhenRetriesExhausted();
        });

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        scheduler.ListenerManager.GetTriggerListeners()
            .Where(listener => listener.Name == RetriesExhaustedPauseListener.ListenerName)
            .Should().ContainSingle("the second call is a no-op, so an occurrence is paused once rather than twice");

        await scheduler.Shutdown();
    }

    [Test]
    public void AJobExecutionExceptionTheJobThrewIsItsOwnReason()
    {
        RetriesExhaustedPauseListener.ReasonFor(new JobExecutionException("quota exceeded", new InvalidOperationException("inner")))
            .Should().Be("quota exceeded",
                "a job that wrapped its own failure said what it meant in the wrapper");

        RetriesExhaustedPauseListener.ReasonFor(new JobExecutionException(new InvalidOperationException("the disk is full")))
            .Should().Be("the disk is full", "a wrapper with no words of its own says its cause's");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static ServiceProvider BuildScheduler(string name, Action<IQuartzBuilder>? configure = null, bool addJobs = true)
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = "pause-reason-" + name + "-" + Guid.NewGuid().ToString("N"));

            if (addJobs)
            {
                quartz.AddJob<IdleJob>(job => job.WithIdentity("report", "reports").StoreDurably());
                quartz.AddTrigger(trigger => trigger.ForJob("report", "reports").WithIdentity("nightly", "reports")
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .StartAt(DateTimeOffset.UtcNow.AddHours(1)));
                quartz.AddJob<IdleJob>(job => job.WithIdentity("import", "imports").StoreDurably());
                quartz.AddTrigger(trigger => trigger.ForJob("import", "imports").WithIdentity("hourly", "imports")
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .StartAt(DateTimeOffset.UtcNow.AddHours(1)));
            }

            configure?.Invoke(quartz);
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Polls <paramref name="condition" /> until it holds, failing with <paramref name="because" /> when it
    /// has not after thirty seconds rather than hanging the run.
    /// </summary>
    private static async Task WaitUntil(Func<Task<bool>> condition, string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting: " + because);
            }

            await Task.Delay(50);
        }
    }

    /// <summary>What a scheduler listener heard about pauses, in order.</summary>
    private sealed class PauseRecorder : ISchedulerListener
    {
        private readonly ConcurrentQueue<string> events = new();

        public List<string> Events => [.. events];

        public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            events.Enqueue("trigger " + triggerKey);
            return default;
        }

        public ValueTask TriggersPaused(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default)
        {
            events.Enqueue(triggerGroup is null ? "all triggers" : "trigger group " + triggerGroup);
            return default;
        }

        public ValueTask JobPaused(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
        {
            events.Enqueue("job " + jobKey);
            return default;
        }

        public ValueTask JobsPaused(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default)
        {
            events.Enqueue("job group " + jobGroup);
            return default;
        }
    }

    /// <summary>Never runs in these cases: its triggers are an hour away.</summary>
    public sealed class IdleJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Fails every time, counting the attempts.</summary>
    public sealed class FailingJob : IJob
    {
        internal static int Attempts;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Attempts);
            throw new InvalidOperationException("the upstream system is down");
        }
    }
}
