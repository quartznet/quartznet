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

using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The shape of what a scheduler publishes to a tracer: how many traces it opens, and what each span's
/// parent is.
/// </summary>
/// <remarks>
/// <para>
/// This is #3797. A scheduler that ran for a day produced two trace ids and a single tree thousands of
/// spans deep, because every span the scheduler's own loop opened became the parent of the next one and
/// every firing hung off whichever one was current when it was dispatched. Nothing asserted otherwise:
/// the rest of the observability suite reads tags off one span at a time, and a span's parent is not a
/// tag.
/// </para>
/// <para>
/// Two independent defects made that trace, and both are pinned below. A store span was started in the
/// synchronous part of the decorator and stopped inside the asynchronous one, so
/// <see cref="Activity.Current" /> was written onto the caller's execution context and never taken back
/// off it — <c>Activity.Stop</c> restored the parent onto the continuation's context, which is thrown
/// away. And the execute span took whatever activity was ambient on the worker as its parent, so the
/// leak above, or merely starting the scheduler inside a request, decided the firing's trace.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class TraceTopologyTest
{
    private const string ExecuteSpan = "Quartz.Job.Execute";
    private const string SchedulerNameTag = "quartz.scheduler.name";

    private readonly List<Activity> stoppedActivities = [];

    private ActivityListener activityListener;

    [SetUp]
    public void SetUp()
    {
        lock (stoppedActivities)
        {
            stoppedActivities.Clear();
        }

        activityListener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stoppedActivities)
                {
                    stoppedActivities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
    }

    [TearDown]
    public void TearDown() => activityListener?.Dispose();

    /// <summary>
    /// The reported symptom, counted the way the report counted it.
    /// </summary>
    [Test]
    public async Task ARepeatingTrigger_OpensOneTracePerFiring()
    {
        Run run = await RunRepeatedly(firings: 5);

        List<Activity> executions = run.Spans.Where(a => a.OperationName == ExecuteSpan).ToList();

        executions.Should().HaveCountGreaterThanOrEqualTo(5, "the trigger fired at least this many times");

        executions.Should().AllSatisfy(a => a.ParentSpanId.Should().Be(default(ActivitySpanId),
            "a firing is its own trace root — which is what the documentation promises, and what makes "
            + "the link back to the scheduling call the right shape for the wait between them"));

        executions.Select(a => a.TraceId).Should().OnlyHaveUniqueItems(
            "every firing is a trace of its own, so a job's own spans are the only company it keeps — "
            + "they used to chain onto one another and a day of firings was one unreadable tree");
    }

    /// <summary>
    /// The leak underneath it: the scheduler's loop is a single asynchronous flow that lives as long as
    /// the process, so one span left current in it is inherited by every span opened afterwards.
    /// </summary>
    [Test]
    public async Task TheSchedulerLoop_NeverParentsOneQuartzSpanOntoAnother()
    {
        Run run = await RunRepeatedly(firings: 5);

        Dictionary<ActivitySpanId, Activity> bySpanId = run.Spans
            .DistinctBy(a => a.SpanId)
            .ToDictionary(a => a.SpanId);

        List<Activity> nested = run.Spans.Where(a => bySpanId.ContainsKey(a.ParentSpanId)).ToList();

        nested.Should().BeEmpty(
            "the job does nothing, so there is nothing legitimately nested — every span here is opened "
            + "by the scheduler's loop or by the shell around a firing, and each is its own root. A "
            + "parent among them is the chain that made a scheduler emit one trace per process");

        run.Spans.Select(a => a.TraceId).Distinct().Should().HaveCount(run.Spans.Count,
            "one span, one trace: the report saw 1,499 executions arrive as two traces");
    }

    /// <summary>
    /// A scheduler is usually started from inside something that is itself traced — a hosted service, an
    /// HTTP request that spun a tenant up. The loop's task captures the execution context of whoever
    /// called <c>Start</c>, so without clearing it that one span is the ancestor of everything the
    /// scheduler ever emits.
    /// </summary>
    /// <remarks>
    /// The store calls <c>Start</c> itself makes — scheduling what the container declared — stay in the
    /// caller's trace, and should: the caller made them, and they end when the call does. What must not
    /// is anything the loop goes on to do by itself afterwards.
    /// </remarks>
    [Test]
    public async Task AFiringStartedInsideACallersTrace_IsStillItsOwnRoot()
    {
        using Activity caller = new Activity("caller.starts.the.scheduler").SetIdFormat(ActivityIdFormat.W3C).Start();

        Run run = await RunRepeatedly(firings: 2, startUnder: caller);

        List<Activity> loop = run.Spans
            .Where(a => a.OperationName is ExecuteSpan
                or OperationName.JobStore.AcquireNextTriggers
                or OperationName.JobStore.TriggersFired
                or OperationName.JobStore.TriggeredJobComplete)
            .ToList();

        loop.Should().NotBeEmpty("the loop acquired, fired and completed at least one trigger");
        loop.Should().AllSatisfy(a => a.TraceId.Should().NotBe(caller.TraceId,
            "the loop outlives the call that started it by the whole life of the process, and a trace "
            + "that never ends is a trace no backend will render"));

        run.Spans.Where(a => a.OperationName == ExecuteSpan).Should().NotBeEmpty()
            .And.AllSatisfy(a => a.ParentSpanId.Should().Be(default(ActivitySpanId)));

        run.Spans.Where(a => a.TraceId == caller.TraceId).Select(a => a.OperationName).Distinct()
            .Should().BeEquivalentTo([OperationName.JobStore.ScheduleJob],
                "the only thing the caller did was ask the scheduler to start, which scheduled the "
                + "trigger the container declared");
    }

    /// <summary>
    /// The link is what connects a firing to the call that scheduled it, and it has to survive the
    /// firing becoming a root.
    /// </summary>
    [Test]
    public async Task AFiringThatIsARoot_StillLinksBackToWhatScheduledIt()
    {
        using Activity scheduling = new Activity("caller.schedules").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity.Current = null;

        Run run = await RunRepeatedly(firings: 1, scheduleUnder: scheduling);

        Activity execution = run.Spans.Should().ContainSingle(a => a.OperationName == ExecuteSpan).Subject;

        execution.ParentSpanId.Should().Be(default(ActivitySpanId), "the firing is a root");
        execution.Links.Should().ContainSingle().Which.Context.SpanId.Should().Be(scheduling.SpanId,
            "being a root is what makes the link the only way back to the scheduling call, so the two "
            + "belong together rather than being alternatives");
    }

    /// <summary>
    /// A refused firing is a firing, so it is a root and it links back the same way.
    /// </summary>
    /// <remarks>
    /// The veto span used to be opened with <c>StartActivity</c>, which adopts whatever is ambient — so
    /// it joined the loop's chain exactly as an execution did, and carried no link at all.
    /// </remarks>
    [Test]
    public async Task AVetoedFiring_IsARootAndLinksBackToWhatScheduledIt()
    {
        using Activity scheduling = new Activity("caller.schedules").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity.Current = null;

        Run run = await RunRepeatedly(firings: 1, scheduleUnder: scheduling, veto: true);

        Activity vetoed = run.Spans.Should().ContainSingle(a => a.OperationName == "Quartz.Job.Veto").Subject;

        vetoed.ParentSpanId.Should().Be(default(ActivitySpanId),
            "a veto is a firing that a trigger listener turned down, and it belongs in a trace of its own "
            + "for the same reason an execution does");
        vetoed.Links.Should().ContainSingle().Which.Context.SpanId.Should().Be(scheduling.SpanId,
            "walking back to the call that scheduled a firing is worth as much when the firing was "
            + "refused as when it ran");

        run.Spans.Should().NotContain(a => a.OperationName == ExecuteSpan, "the job never ran");
    }

    /// <summary>
    /// Runs one scheduler until its trigger has fired <paramref name="firings" /> times, and hands back
    /// the spans that scheduler published.
    /// </summary>
    /// <remarks>
    /// Spans are selected by the scheduler's name, which is unique to the run: the listener is attached
    /// to the source rather than to an instance, so a scheduler another fixture left running publishes
    /// onto the same list.
    /// </remarks>
    private async Task<Run> RunRepeatedly(int firings, Activity startUnder = null, Activity scheduleUnder = null, bool veto = false)
    {
        string id = Guid.NewGuid().ToString("N");
        string schedulerName = $"topology-{id}";
        JobKey jobKey = new($"job-{id}", $"job-group-{id}");

        CountingListener counting = new(firings);

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = schedulerName);
            quartz.AddJobListener(counting);
            if (veto)
            {
                quartz.AddTriggerListener(new VetoingTriggerListener());
            }

            quartz.AddJob<NoOpJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<NoOpJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"trigger-{id}")
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromMilliseconds(50))
                    .WithRepeatCount(firings - 1))
                .StartNow());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await Under(scheduleUnder, () => provider.GetRequiredService<ISchedulerFactory>().GetScheduler().AsTask());
        try
        {
            await Under(startUnder, () => scheduler.Start().AsTask());

            Task finished = await Task.WhenAny(counting.Reached, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(counting.Reached, "the trigger should have fired {0} times", firings);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        lock (stoppedActivities)
        {
            return new Run(stoppedActivities
                .Where(a => Equals(a.GetTagItem(SchedulerNameTag), schedulerName))
                .ToList());
        }
    }

    /// <summary>
    /// Runs one call with a chosen ambient activity, and nothing else with it — an <c>AsyncLocal</c>
    /// written inside an async method does not escape it.
    /// </summary>
    private static async Task<T> Under<T>(Activity activity, Func<Task<T>> call)
    {
        if (activity is not null)
        {
            Activity.Current = activity;
        }

        return await call();
    }

    private static async Task Under(Activity activity, Func<Task> call)
    {
        if (activity is not null)
        {
            Activity.Current = activity;
        }

        await call();
    }

    private sealed record Run(List<Activity> Spans);

    /// <summary>
    /// Signals once the shell has finished with the agreed number of firings, which is after the span
    /// of each has been closed.
    /// </summary>
    private sealed class CountingListener : IJobListener
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int target;
        private int seen;

        public CountingListener(int target) => this.target = target;

        public string Name => "counting";

        public Task Reached => reached.Task;

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Count();
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            Count();
            return default;
        }

        private void Count()
        {
            if (Interlocked.Increment(ref seen) >= target)
            {
                reached.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Refuses every fire, so the shell takes the veto path instead of executing.
    /// </summary>
    private sealed class VetoingTriggerListener : ITriggerListener
    {
        public string Name => "vetoing";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => new(true);
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
