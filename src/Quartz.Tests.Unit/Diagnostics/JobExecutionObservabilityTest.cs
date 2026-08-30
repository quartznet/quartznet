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
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// What one job execution publishes to an application's telemetry: the meter measurements an
/// OpenTelemetry exporter would pick up, and the activity a tracer would see.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler under test is always built the way applications build one — <c>services.AddQuartz()</c>
/// and the container — because that is precisely where the metrics used to be missing. Configuring the
/// meter was wired to the properties-based factory alone, so every scheduler registered through
/// dependency injection published nothing at all, and nothing noticed because the feature had no tests.
/// The instruments belong to the container now rather than to the process, so a scheduler built any other
/// way cannot stand in for one built this way: what these tests assert is that a scheduler built the way
/// nearly every application builds one emits.
/// </para>
/// <para>
/// Measurements are matched by the job key, which is unique per test, so a scheduler left running by
/// another fixture cannot be mistaken for this one's.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class JobExecutionObservabilityTest
{
    private const string ExecuteActive = "quartz.job.execution.active";
    private const string ExecuteDuration = "quartz.job.execution.duration";

    // Instrument and attribute names are spelled out here rather than read from Quartz, because the wire
    // name is the contract an exporter and a dashboard are written against: reading them from the same
    // constant the product publishes them from would let a rename pass unnoticed.
    //
    // error.type is OpenTelemetry's conventional attribute for what an operation failed with, and the one
    // attribute Quartz does not namespace, because every instrumented failure in the process spells it
    // this way.
    private const string ErrorTypeTag = "error.type";

    private const string SchedulerNameTag = "quartz.scheduler.name";
    private const string SchedulerIdTag = "quartz.scheduler.id";
    private const string FireInstanceIdTag = "quartz.fire.instance.id";
    private const string TriggerGroupTag = "quartz.trigger.group";
    private const string TriggerNameTag = "quartz.trigger.name";
    private const string JobTypeTag = "quartz.job.type";
    private const string JobGroupTag = "quartz.job.group";
    private const string JobNameTag = "quartz.job.name";
    private const string ExecutionGroupTag = "quartz.execution.group";

    private readonly List<RecordedMeasurement> measurements = [];
    private readonly List<Activity> stoppedActivities = [];

    private MeterListener meterListener;
    private ActivityListener activityListener;

    [SetUp]
    public void SetUp()
    {
        lock (measurements)
        {
            measurements.Clear();
        }

        lock (stoppedActivities)
        {
            stoppedActivities.Clear();
        }

        meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meterListener.Start();

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
            }
        };
        ActivitySource.AddActivityListener(activityListener);
    }

    [TearDown]
    public void TearDown()
    {
        meterListener?.Dispose();
        activityListener?.Dispose();
    }

    /// <summary>
    /// The regression test for the metrics that a scheduler built by the container never published.
    /// </summary>
    [Test]
    public async Task JobExecution_ThroughDependencyInjection_PublishesExecutionMetrics()
    {
        Execution execution = await RunJob<SucceedingJob>();

        List<RecordedMeasurement> published = MeasurementsFor(execution.JobKey);

        published.Should().NotBeEmpty(
            "a scheduler registered with AddQuartz must publish job execution metrics — this is the whole "
            + "of what an application's OpenTelemetry exporter sees, and it saw nothing while configuring "
            + "the meter was wired to one construction path");

        published.Select(m => m.Meter).Should().AllBe(QuartzInstrumentation.MeterName,
            "the meter name is the contract: it is what an exporter is told to subscribe to");

        published.Select(m => m.Instrument).Distinct().Should().BeEquivalentTo([ExecuteActive, ExecuteDuration],
            "an execution is two instruments and no more — the histogram's own count is how many executions "
            + "there were, and its error.type-tagged subset is how many failed, so the counters that used to "
            + "report those two numbers were extra writes per fire for something the exporter already had");

        RecordedMeasurement duration = published.Should().ContainSingle(m => m.Instrument == ExecuteDuration).Subject;
        duration.Value.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(30);
        duration.Unit.Should().Be("s",
            "OpenTelemetry records a duration in seconds, and a histogram's default bucket boundaries are "
            + "chosen for seconds — milliseconds piled every execution over ten seconds into the last bucket");

        List<RecordedMeasurement> active = published.Where(m => m.Instrument == ExecuteActive).ToList();

        active.Should().HaveCount(2,
            "the in-progress count is incremented when the job starts and decremented when it ends");
        active.Sum(m => m.Value).Should().Be(0, "a finished job leaves nothing in progress");
        active.Should().AllSatisfy(m => m.InstrumentType.Should().Be<UpDownCounter<long>>(
            "a count of what is running goes down as often as it goes up, and an exporter aggregates a "
            + "Counter as monotonic — which is what made the decrement something it could drop"));
        active.Should().AllSatisfy(m => m.Unit.Should().Be("{job}",
            "UCUM's annotation form is how OpenTelemetry spells a dimensionless count of a thing; \"ea\" is "
            + "not a UCUM unit at all"));

        foreach (RecordedMeasurement measurement in published)
        {
            measurement.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerNameTag, execution.SchedulerName))
                .And.Contain(new KeyValuePair<string, object>(SchedulerIdTag, execution.SchedulerInstanceId))
                .And.Contain(new KeyValuePair<string, object>(TriggerGroupTag, execution.TriggerKey.Group))
                .And.Contain(new KeyValuePair<string, object>(TriggerNameTag, execution.TriggerKey.Name))
                .And.Contain(new KeyValuePair<string, object>(JobGroupTag, execution.JobKey.Group))
                .And.Contain(new KeyValuePair<string, object>(JobNameTag, execution.JobKey.Name));

            measurement.Tags.Should().HaveCount(6,
                "an execution is identified by the scheduler that ran it — name and node id both — its "
                + "trigger and its job, and nothing else is added to it");

            measurement.Tags.Should().NotContainKey(ExecutionGroupTag,
                "this trigger names no execution group, and an attribute that is absent is not the same "
                + "series as one that is present and empty");
        }
    }

    /// <summary>
    /// The tag that tells two nodes of one cluster apart. Two schedulers sharing a name is what a cluster
    /// <em>is</em>, so a measurement carrying only the name cannot answer which node made it.
    /// </summary>
    [Test]
    public async Task ExecutionMeasurements_CarryTheSchedulerInstanceId()
    {
        Execution execution = await RunJob<SucceedingJob>();

        MeasurementsFor(execution.JobKey).Should().NotBeEmpty().And.AllSatisfy(m =>
            m.Tags.Should().ContainKey(SchedulerIdTag).WhoseValue.Should().Be(execution.SchedulerInstanceId,
                "the id is what names the node, and it was on spans only — so two nodes of one cluster "
                + "published measurements that no dashboard could separate again"));
    }

    /// <summary>
    /// The execution group is a dimension only for the triggers that have one.
    /// </summary>
    [Test]
    public async Task ExecutionSignals_CarryTheExecutionGroup_WhenTheTriggerNamesOne()
    {
        Execution execution = await RunJob<SucceedingJob>(executionGroup: "reports");

        MeasurementsFor(execution.JobKey).Should().NotBeEmpty().And.AllSatisfy(m =>
            m.Tags.Should().ContainKey(ExecutionGroupTag).WhoseValue.Should().Be("reports",
                "an execution group is the bucket a thread limit is applied per, so it is the dimension "
                + "'which bucket saturated' has to be asked in"));

        ActivityFor(execution.JobKey).GetTagItem(ExecutionGroupTag).Should().Be("reports",
            "the span and the measurements answer the same question the same way");
    }

    /// <summary>
    /// Every attribute Quartz defines is spelled under <c>quartz.</c>, and the constants say so.
    /// </summary>
    [Test]
    public void AttributeNames_AreNamespacedUnderQuartz()
    {
        ActivityTags.SchedulerName.Should().Be(SchedulerNameTag);
        ActivityTags.SchedulerId.Should().Be(SchedulerIdTag);
        ActivityTags.FireInstanceId.Should().Be(FireInstanceIdTag);
        ActivityTags.TriggerGroup.Should().Be(TriggerGroupTag);
        ActivityTags.TriggerName.Should().Be(TriggerNameTag);
        ActivityTags.JobType.Should().Be(JobTypeTag);
        ActivityTags.JobGroup.Should().Be(JobGroupTag);
        ActivityTags.JobName.Should().Be(JobNameTag);
        ActivityTags.ExecutionGroup.Should().Be(ExecutionGroupTag);
        ActivityTags.TriggerCount.Should().Be("quartz.jobstore.trigger.count");
        ActivityTags.BatchSize.Should().Be("quartz.jobstore.batch.size");
        ActivityTags.JobStoreOperation.Should().Be("quartz.jobstore.operation");
        ActivityTags.RecoveredInstanceId.Should().Be("quartz.cluster.recovered.instance.id");
    }

    /// <summary>
    /// The two strings an application types to subscribe are published, so nobody has to write "Quartz".
    /// </summary>
    [Test]
    public void SubscriptionNames_ArePublicConstants()
    {
        QuartzInstrumentation.ActivitySourceName.Should().Be("Quartz");
        QuartzInstrumentation.MeterName.Should().Be("Quartz");
    }

    /// <summary>
    /// The measurements a container's own <c>IMeterFactory</c> publishes, read the way an application's
    /// tests read them.
    /// </summary>
    /// <remarks>
    /// The meter used to be a static created once per process, so every scheduler in every container
    /// published to the same one and <c>MetricCollector</c> — which collects the instruments belonging to
    /// one factory — had nothing to collect. It is built from the container's factory now, which is what
    /// makes this test possible at all, and the scheduler that ran the job is a tag rather than something
    /// the reader has to infer.
    /// </remarks>
    [Test]
    public async Task ExecutionMetrics_AreCollectableThroughTheContainersMeterFactory()
    {
        string id = Guid.NewGuid().ToString("N");
        string schedulerName = $"collected-{id}";
        ExecutionCompletionListener completion = new();

        ServiceCollection services = new();
        services.AddMetrics();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = schedulerName);
            quartz.AddJobListener(completion);
            quartz.ScheduleJob<SucceedingJob>(
                trigger => trigger.WithIdentity($"trigger-{id}").StartNow(),
                job => job.WithIdentity($"job-{id}"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        using MetricCollector<double> collector = new(
            provider.GetRequiredService<IMeterFactory>(),
            QuartzInstrumentation.MeterName,
            ExecuteDuration);

        // The scheduler is injected rather than built from its factory, which is the other half of what
        // this fixture is for: an application asks the container for both of these.
        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await collector.WaitForMeasurementsAsync(minCount: 1).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        CollectedMeasurement<double> measurement = collector.LastMeasurement;
        measurement.Should().NotBeNull("the container's meter factory published this execution");
        measurement.Value.Should().BeGreaterThanOrEqualTo(0);
        collector.Instrument.Unit.Should().Be("s", "the histogram records seconds");
        measurement.Tags.Should().ContainKey(SchedulerNameTag)
            .WhoseValue.Should().Be(schedulerName,
                "a process can run several schedulers, and without the name their measurements are one "
                + "series a dashboard cannot separate again");
    }

    [Test]
    public async Task FailingJobExecution_IsCountedByTheDurationHistogramAndTaggedWithTheFailure()
    {
        Execution execution = await RunJob<ThrowingJob>();

        List<RecordedMeasurement> published = MeasurementsFor(execution.JobKey);

        List<RecordedMeasurement> active = published.Where(m => m.Instrument == ExecuteActive).ToList();

        active.Sum(m => m.Value).Should().Be(0,
            "a job that throws leaves nothing in progress either — the decrement is not on the happy path only");
        active.Should().AllSatisfy(m => m.Tags.Should().NotContainKey(ErrorTypeTag,
            "an up-down counter is aggregated per attribute set, so the decrement has to carry exactly the "
            + "tags the increment carried or the series never comes back to zero"));

        published.Should().ContainSingle(m => m.Instrument == ExecuteDuration,
            "a job that throws still executed, and one measurement is what an execution records — a failure "
            + "is the same measurement wearing error.type, which is how an exporter counts the failures")
            .Which.Tags.Should().ContainKey(ErrorTypeTag,
                "and a failed run's duration is worth telling apart from a successful one's");
    }

    /// <summary>
    /// The failure attribute says what went wrong, not only that something did.
    /// </summary>
    /// <remarks>
    /// The tag used to be added to <c>_tagList.Value</c>, and <see cref="Nullable{T}.Value"/> hands back a
    /// copy of the struct: it went onto a temporary that was thrown away on the next line, where a second
    /// copy — still the four identity tags — was what the instrument was given. Once it did arrive it named
    /// the <see cref="JobExecutionException"/> the run shell wraps anything a job throws in, which is the
    /// same answer for nearly every failure there is; what it reports now is the exception the job threw,
    /// found by unwrapping that pair of wrappers.
    /// </remarks>
    [Test]
    public async Task FailingJobExecution_TagsTheErrorWithTheExceptionTheJobThrew()
    {
        Execution execution = await RunJob<ThrowingJob>();

        RecordedMeasurement failed = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteDuration);

        failed.Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(InvalidOperationException).FullName,
                "the run shell wraps what a job throws as JobExecutionException -> "
                + "JobExecutionProcessException -> the cause, and naming either wrapper would tell an "
                + "exporter only that Quartz caught something, which it already knew from the measurement");
    }

    /// <summary>
    /// Unwrapping stops at what the job threw, so a job that raises a <see cref="JobExecutionException"/>
    /// deliberately is reported as having thrown one.
    /// </summary>
    [Test]
    public async Task JobThrowingJobExecutionException_TagsTheErrorWithThatType()
    {
        Execution execution = await RunJob<DeliberatelyFailingJob>();

        RecordedMeasurement failed = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteDuration);

        failed.Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(JobExecutionException).FullName,
                "a JobExecutionException a job raised itself is not a wrapper the run shell added — there "
                + "is no JobExecutionProcessException under it — and it is what the job chose to say");

        ActivityFor(execution.JobKey).GetTagItem(ErrorTypeTag).Should().Be(typeof(JobExecutionException).FullName,
            "the span and the duration histogram answer the same question the same way");
    }

    [Test]
    public async Task JobExecution_EmitsActivityWithJobAndTriggerTags()
    {
        Execution execution = await RunJob<SucceedingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.OperationName.Should().Be("Quartz.Job.Execute");
        activity.Source.Name.Should().Be(QuartzInstrumentation.ActivitySourceName,
            "the source name is what a tracer is told to subscribe to");
        activity.Kind.Should().Be(ActivityKind.Internal);
        activity.Status.Should().Be(ActivityStatusCode.Unset, "the job succeeded");

        activity.GetTagItem(SchedulerNameTag).Should().Be(execution.SchedulerName);
        activity.GetTagItem(SchedulerIdTag).Should().Be(execution.SchedulerInstanceId);
        activity.GetTagItem(JobTypeTag).Should().Be(new JobType(typeof(SucceedingJob)).FullName,
            "the job type is reported the way the scheduler names it — assembly-qualified, without a version");
        activity.GetTagItem(FireInstanceIdTag).Should().Be(execution.FireInstanceId);
        activity.GetTagItem(TriggerGroupTag).Should().Be(execution.TriggerKey.Group);
        activity.GetTagItem(TriggerNameTag).Should().Be(execution.TriggerKey.Name);
        activity.GetTagItem(JobGroupTag).Should().Be(execution.JobKey.Group);
        activity.GetTagItem(JobNameTag).Should().Be(execution.JobKey.Name);
    }

    /// <summary>
    /// The span is opened around the whole pipeline, not around the job alone.
    /// </summary>
    /// <remarks>
    /// Middleware is invoked inside the activity and the meter bracket on purpose: what a log scope, a
    /// tenant lookup or a timeout costs is part of what the firing cost, and a trace that showed the job
    /// but not the middleware wrapped around it would attribute that time to nothing.
    /// </remarks>
    [Test]
    public async Task TheExecutionSpanWrapsTheWholeMiddlewareChain()
    {
        ActivityReadingMiddleware middleware = new();

        Execution execution = await RunJob<SucceedingJob>(middleware: middleware);

        Activity activity = ActivityFor(execution.JobKey);

        middleware.OnTheWayIn.Should().BeSameAs(activity,
            "a middleware is entered after the span has been started, so anything it traces is a child "
            + "of the execution rather than an orphan");
        middleware.OnTheWayOut.Should().BeSameAs(activity,
            "and the span is still open while a middleware's finally block runs, which is where a "
            + "timing or a cleanup middleware does its work");
    }

    /// <summary>
    /// A middleware runs outside the run shell's exception handling, so what it throws is classified as
    /// though the job had thrown it.
    /// </summary>
    [Test]
    public async Task AMiddlewareThatThrows_FailsTheSpanTheWayAJobDoes()
    {
        Execution execution = await RunJob<SucceedingJob>(middleware: new ThrowingMiddleware());

        Activity activity = ActivityFor(execution.JobKey);

        activity.Status.Should().Be(ActivityStatusCode.Error,
            "the job itself succeeded, but the firing did not — and the firing is what the span is about");
        activity.GetTagItem(ErrorTypeTag).Should().Be(typeof(InvalidOperationException).FullName,
            "an exception a middleware lets out is unwrapped and reported exactly like one the job threw");

        MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteDuration)
            .Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(InvalidOperationException).FullName,
                "the histogram and the span agree about what failed, whether the failure came from the "
                + "job or from something wrapped around it");
    }

    /// <summary>
    /// A vetoed fire is a span of its own, and nothing an execution histogram ever hears about.
    /// </summary>
    /// <remarks>
    /// The span's name was <c>Quartz.Job.Vetoed</c> while the constant naming it was <c>Veto</c>; it is
    /// <c>Quartz.Job.Veto</c> now, in the present tense the rest of <see cref="OperationName"/> uses.
    /// </remarks>
    [Test]
    public async Task VetoedExecution_EmitsVetoActivityAndNoExecutionMeasurements()
    {
        Execution execution = await RunJob<SucceedingJob>(veto: true);

        Activity vetoed = ActivityFor(execution.JobKey, "Quartz.Job.Veto");

        vetoed.Source.Name.Should().Be(QuartzInstrumentation.ActivitySourceName);
        vetoed.GetTagItem(SchedulerNameTag).Should().Be(execution.SchedulerName);
        vetoed.GetTagItem(TriggerGroupTag).Should().Be(execution.TriggerKey.Group);
        vetoed.GetTagItem(TriggerNameTag).Should().Be(execution.TriggerKey.Name);
        vetoed.GetTagItem(JobGroupTag).Should().Be(execution.JobKey.Group);
        vetoed.GetTagItem(JobNameTag).Should().Be(execution.JobKey.Name);
        vetoed.GetTagItem(FireInstanceIdTag).Should().Be(execution.FireInstanceId);

        StoppedActivities().Should().NotContain(a =>
                a.OperationName == "Quartz.Job.Execute"
                && Equals(a.GetTagItem(JobNameTag), execution.JobKey.Name),
            "the job never ran, so there is no execution to trace");

        MeasurementsFor(execution.JobKey).Should().BeEmpty(
            "the histogram's count is what an exporter reads as the number of executions, and a fire a "
            + "listener refused is not one — the instruments are started after the veto decision, so a "
            + "vetoed fire never arrives as a zero-duration success");
    }

    [Test]
    public async Task FailingJobExecution_EmitsActivityWithErrorStatusAndException()
    {
        Execution execution = await RunJob<ThrowingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.Events.Should().ContainSingle(e => e.Name == "exception",
            "the exception the job threw is recorded on the span that failed");

        activity.GetTagItem(ErrorTypeTag).Should().Be(typeof(InvalidOperationException).FullName,
            "the span classifies the failure with the attribute the measurement is tagged with, so one "
            + "value finds the failed executions in a trace and in a metric alike");

        RecordedMeasurement failed = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteDuration);
        failed.Tags[ErrorTypeTag].Should().Be(activity.GetTagItem(ErrorTypeTag),
            "the two signals are read together, and disagreeing about what failed is worse than either "
            + "of them being silent");
    }

    // ---------------------------------------------------------------------------------------------
    // The gap between scheduling a job and running it
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The whole of #3524: a firing can be walked back to the call that asked for it, however long ago
    /// that was and whichever node it happened on.
    /// </summary>
    [Test]
    public async Task TheExecuteSpan_LinksBackToTheActivityThatScheduledTheTrigger()
    {
        using Activity scheduling = StartDetachedActivity();
        scheduling.TraceStateString = "quartz=alpha4";

        Execution execution = await RunJob<SucceedingJob>(scheduledUnder: scheduling);

        Activity activity = ActivityFor(execution.JobKey);

        ActivityLink link = activity.Links.Should().ContainSingle(
            "one scheduling call produced this firing, so there is exactly one thing to link back to")
            .Subject;

        link.Context.TraceId.Should().Be(scheduling.TraceId,
            "walking from the firing to the request that asked for it is the point, and the trace id is "
            + "what a backend searches by");
        link.Context.SpanId.Should().Be(scheduling.SpanId,
            "the link names the scheduling span itself, not merely its trace");
        link.Context.TraceState.Should().Be("quartz=alpha4",
            "tracestate is vendor routing information that travels with the context, and dropping it "
            + "silently sends the linked span to a different backend than the one that recorded it");
        link.Context.IsRemote.Should().BeTrue(
            "the context was parsed from stored data rather than created in this process — the same "
            + "thing a server span's context is when it came off the wire");

        activity.ParentSpanId.Should().NotBe(scheduling.SpanId,
            "the scheduling call and the firing are separated by however long the schedule said, and a "
            + "span whose parent is a week away makes a trace no backend can render");
    }

    /// <summary>
    /// The trigger's map, never the job's — so a <c>[PersistJobDataAfterExecution]</c> job can never
    /// write a <c>traceparent</c> forward into its next firing.
    /// </summary>
    [Test]
    public async Task TheTraceContext_IsWrittenOntoTheTriggerUnderTheReservedKeys()
    {
        using Activity scheduling = StartDetachedActivity();

        Execution execution = await RunJob<SucceedingJob>(scheduledUnder: scheduling);

        execution.MergedJobData.Should().ContainKey(SchedulerConstants.TraceParent)
            .WhoseValue.Should().Be(scheduling.Id,
                "the stored value is the W3C traceparent verbatim, so anything that can parse a "
                + "traceparent header can read it");
        execution.MergedJobData.Should().NotContainKey(SchedulerConstants.TraceState,
            "the scheduling activity carried no tracestate, and an empty entry per trigger would be "
            + "storage spent on nothing");
    }

    [Test]
    public async Task AJobScheduledOutsideAnyActivity_CarriesNoLinkAndNoReservedKeys()
    {
        Execution execution = await RunJob<SucceedingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.Links.Should().BeEmpty(
            "there was nothing to link to — a link to a context nobody recorded is worse than none");
        execution.MergedJobData.Should().NotContainKey(SchedulerConstants.TraceParent,
            "an application that never traces must not pay two map entries per trigger for the feature");
    }

    [Test]
    public async Task TurningPropagationOff_LeavesTheTriggerAndTheSpanAlone()
    {
        using Activity scheduling = StartDetachedActivity();

        Execution execution = await RunJob<SucceedingJob>(propagateTraceContext: false, scheduledUnder: scheduling);

        execution.MergedJobData.Should().NotContainKey(SchedulerConstants.TraceParent,
            "opting out is about what reaches the store, so the key is never written in the first place");
        ActivityFor(execution.JobKey).Links.Should().BeEmpty(
            "with nothing stored there is nothing to link to, and the execute span is emitted either way");
    }

    /// <summary>
    /// The reason the write is a write-<em>or-remove</em>.
    /// </summary>
    /// <remarks>
    /// <c>AsOperableTrigger</c> hands the store the very object the caller passed rather than a copy of
    /// it, so a trigger scheduled once inside a request and again outside one would otherwise keep
    /// pointing at the first request's trace — and a stale link reads exactly like a true one.
    /// </remarks>
    [Test]
    public async Task ReschedulingTheSameTriggerObjectOutsideAnActivity_DropsTheStaleTraceParent()
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"job-group-{id}");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"stale-traceparent-{id}");
            quartz.AddJob<SucceedingJob>(job => job.WithIdentity(jobKey).StoreDurably());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        // Never started: this is about what the scheduling call writes, and a firing would delete the
        // one-shot trigger out from under the second half of the test.
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity($"trigger-{id}", $"trigger-group-{id}")
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();

        using (Activity scheduling = StartDetachedActivity())
        {
            Activity previous = Activity.Current;
            Activity.Current = scheduling;
            try
            {
                await scheduler.ScheduleJob(trigger);
            }
            finally
            {
                Activity.Current = previous;
            }

            trigger.JobDataMap.Should().ContainKey(SchedulerConstants.TraceParent)
                .WhoseValue.Should().Be(scheduling.Id, "the first scheduling call happened inside a trace");
        }

        await scheduler.ScheduleJob(trigger, new ScheduleJobOptions { Replace = true });

        trigger.JobDataMap.Should().NotContainKey(SchedulerConstants.TraceParent,
            "the second call was made outside any activity, and the scheduler stores the trigger object "
            + "it was handed rather than a copy — leaving the old value would link every future firing "
            + "back to a trace that has nothing to do with it");
    }

    /// <summary>
    /// Rescheduling is a scheduling call, so the firing links to the call that moved it rather than to
    /// the one that first put it there.
    /// </summary>
    [Test]
    public async Task ReschedulingInsideAnActivity_LinksTheFiringToTheReschedule()
    {
        using Activity first = StartDetachedActivity();
        using Activity second = StartDetachedActivity();

        Execution execution = await RescheduleAndRun(scheduledUnder: first, rescheduledUnder: second);

        ActivityLink link = ActivityFor(execution.JobKey).Links.Should().ContainSingle(
            "one call decided when this firing happens, so there is one thing to link back to").Subject;

        link.Context.SpanId.Should().Be(second.SpanId,
            "the reschedule is what decided when this firing happens, and that is the call worth "
            + "walking back to");
        link.Context.TraceId.Should().NotBe(first.TraceId,
            "a replacement trigger rebuilt from the original carries the original's job data map, "
            + "reserved keys included — so the traceparent the first call left has to be overwritten "
            + "rather than kept, or the firing points at a trace that did not schedule it");
    }

    [Test]
    public async Task ReschedulingOutsideAnyActivity_DropsTheLinkTheFirstSchedulingLeft()
    {
        using Activity first = StartDetachedActivity();

        Execution execution = await RescheduleAndRun(scheduledUnder: first, rescheduledUnder: null);

        ActivityFor(execution.JobKey).Links.Should().BeEmpty(
            "nothing traced the call that decided when this firing happens, and inheriting the earlier "
            + "call's trace would be a link that reads true and is not");
        execution.MergedJobData.Should().NotContainKey(SchedulerConstants.TraceParent,
            $"the replacement carried the first call's key over, so {nameof(SchedulerConstants.TraceParent)} has to be removed rather than left");
    }

    /// <summary>
    /// Schedules a trigger for an hour away under one activity, moves it to now under another, and runs
    /// the firing that results.
    /// </summary>
    /// <remarks>
    /// The replacement is rebuilt from the original with <c>GetTriggerBuilder</c>, which is how an
    /// application reschedules one — and which copies the original's job data map, reserved keys
    /// included. That copy is what makes these tests about overwriting a stale context rather than about
    /// writing to an empty map.
    /// </remarks>
    private static async Task<Execution> RescheduleAndRun(Activity scheduledUnder, Activity rescheduledUnder)
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"job-group-{id}");
        TriggerKey triggerKey = new($"trigger-{id}", $"trigger-group-{id}");

        ExecutionCompletionListener completion = new();

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"reschedule-{id}");
            quartz.AddJobListener(completion);
            quartz.AddJob<SucceedingJob>(job => job.WithIdentity(jobKey).StoreDurably());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            ITrigger original = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();

            await Under(scheduledUnder, () => scheduler.ScheduleJob(original).AsTask());

            ITrigger replacement = original.GetTriggerBuilder().StartNow().Build();

            await Under(rescheduledUnder, () => scheduler.RescheduleJob(triggerKey, replacement).AsTask());

            await scheduler.Start();

            Task finished = await Task.WhenAny(completion.Executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(completion.Executed, "the rescheduled job should have run");

            return new Execution(
                jobKey,
                triggerKey,
                scheduler.SchedulerName,
                scheduler.SchedulerInstanceId,
                await completion.Executed,
                completion.MergedJobData);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Runs one call with a chosen ambient activity, and nothing else with it.
    /// </summary>
    /// <remarks>
    /// An <c>AsyncLocal</c> written inside an async method does not escape it, so the assignment reaches
    /// everything this awaits and nothing the caller goes on to do — which is what keeps the activity
    /// from being current when the scheduler is started.
    /// </remarks>
    private static async Task Under(Activity activity, Func<Task> call)
    {
        Activity.Current = activity;
        await call();
    }

    /// <summary>
    /// A running W3C activity that is deliberately not ambient.
    /// </summary>
    /// <remarks>
    /// It is the shape a caller's span has from the scheduler's point of view: the worker threads that
    /// run the job never captured the caller's execution context, so nothing but the stored
    /// <c>traceparent</c> connects the two. Starting an activity makes it current, so this puts
    /// <see cref="Activity.Current" /> straight back — it cannot simply be stopped instead, because a
    /// finished activity is one <see cref="Activity.Current" /> refuses to be set to.
    /// </remarks>
    private static Activity StartDetachedActivity()
    {
        Activity activity = new Activity("caller.schedules").SetIdFormat(ActivityIdFormat.W3C).Start();
        Activity.Current = null;
        return activity;
    }

    /// <summary>
    /// Builds a scheduler the way an application does, runs the job its trigger fires exactly once, and
    /// shuts everything down before returning — so an assertion never races the execution it is about.
    /// </summary>
    private static async Task<Execution> RunJob<TJob>(
        bool veto = false,
        string executionGroup = null,
        IJobExecutionMiddleware middleware = null,
        bool propagateTraceContext = true,
        Activity scheduledUnder = null) where TJob : IJob
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"job-group-{id}");
        TriggerKey triggerKey = new($"trigger-{id}", $"trigger-group-{id}");

        // The job's own signal would arrive before the shell has recorded anything, since the metrics and
        // the activity are both closed after Execute returns. A job listener is notified after that.
        ExecutionCompletionListener completion = new();

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = $"observability-{id}";
                options.PropagateTraceContext = propagateTraceContext;
            });
            quartz.AddJobListener(completion);
            if (veto)
            {
                quartz.AddTriggerListener(new VetoingTriggerListener());
            }

            if (middleware is not null)
            {
                quartz.AddJobMiddleware(middleware);
            }
            quartz.AddJob<TJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<TJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity(triggerKey)
                .WithExecutionGroup(executionGroup)
                .StartNow());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await CreateScheduler(provider, scheduledUnder);
        try
        {
            await scheduler.Start();

            Task finished = await Task.WhenAny(completion.Executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(completion.Executed,
                veto ? "the scheduled job should have been vetoed" : "the scheduled job should have run");

            return new Execution(
                jobKey,
                triggerKey,
                scheduler.SchedulerName,
                scheduler.SchedulerInstanceId,
                await completion.Executed,
                completion.MergedJobData);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Resolves the scheduler — which is when the container's declared jobs and triggers are scheduled —
    /// under a chosen ambient activity.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="Activity.Current" /> around this one call rather than wrapping the whole of
    /// <c>RunJob</c> in a <c>using</c> is what keeps the scheduling activity out of the execution: an
    /// activity that was still current at <c>Start()</c> would be captured by the scheduler's worker and
    /// become the execute span's *parent*, which is the very thing this feature refuses to do.
    /// </remarks>
    private static async Task<IScheduler> CreateScheduler(ServiceProvider provider, Activity scheduledUnder)
    {
        Activity previous = Activity.Current;
        Activity.Current = scheduledUnder;
        try
        {
            return await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
    {
        // The span is only valid for the duration of the callback, and the callback runs on whichever
        // thread published the measurement — a scheduler worker, not the test's.
        Dictionary<string, object> copy = new(tags.Length, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        lock (measurements)
        {
            // The instrument's own type is what an exporter reads to decide how to aggregate the values:
            // a Counter is a monotonic sum, an UpDownCounter is not. Its unit is read the same way, and it
            // is what decides whether a duration lands in sensible histogram buckets.
            measurements.Add(new RecordedMeasurement(instrument.Meter.Name, instrument.Name, instrument.GetType(), instrument.Unit, value, copy));
        }
    }

    private List<RecordedMeasurement> MeasurementsFor(JobKey jobKey)
    {
        lock (measurements)
        {
            return measurements
                .Where(m => Equals(m.Tags.GetValueOrDefault(JobNameTag), jobKey.Name))
                .ToList();
        }
    }

    private Activity ActivityFor(JobKey jobKey, string operationName = "Quartz.Job.Execute")
    {
        lock (stoppedActivities)
        {
            return stoppedActivities.Should().ContainSingle(a =>
                    a.OperationName == operationName
                    && Equals(a.GetTagItem(JobNameTag), jobKey.Name))
                .Subject;
        }
    }

    private List<Activity> StoppedActivities()
    {
        lock (stoppedActivities)
        {
            return [.. stoppedActivities];
        }
    }

    private sealed record RecordedMeasurement(
        string Meter,
        string Instrument,
        Type InstrumentType,
        string Unit,
        double Value,
        Dictionary<string, object> Tags);

    private sealed record Execution(
        JobKey JobKey,
        TriggerKey TriggerKey,
        string SchedulerName,
        string SchedulerInstanceId,
        string FireInstanceId,
        JobDataMap MergedJobData);

    /// <summary>
    /// Signals once the shell has finished with the execution, which is after both the activity and the
    /// meter measurements have been closed out — or once a fire has been vetoed, which is the only other
    /// way a shell reaches an end a listener hears about.
    /// </summary>
    private sealed class ExecutionCompletionListener : IJobListener
    {
        private readonly TaskCompletionSource<string> executed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "execution-completion";

        public Task<string> Executed => executed.Task;

        /// <summary>
        /// What the firing actually carried, which is how a test asserts on the reserved keys a scheduler
        /// wrote onto the trigger without having to reach back into the store for the row.
        /// </summary>
        public JobDataMap MergedJobData { get; private set; }

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            MergedJobData = context.MergedJobDataMap;
            executed.TrySetResult(context.FireInstanceId);
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            MergedJobData = context.MergedJobDataMap;
            executed.TrySetResult(context.FireInstanceId);
            return default;
        }
    }

    /// <summary>
    /// Reads the ambient <see cref="Activity" /> on both sides of the rest of the pipeline.
    /// </summary>
    public sealed class ActivityReadingMiddleware : IJobExecutionMiddleware
    {
        public Activity OnTheWayIn { get; private set; }

        public Activity OnTheWayOut { get; private set; }

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            OnTheWayIn = Activity.Current;
            try
            {
                await next(context, cancellationToken);
            }
            finally
            {
                OnTheWayOut = Activity.Current;
            }
        }
    }

    /// <summary>
    /// Fails the firing without the job having anything to do with it.
    /// </summary>
    public sealed class ThrowingMiddleware : IJobExecutionMiddleware
    {
        public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("this middleware fails on purpose");
        }
    }

    /// <summary>
    /// Refuses every fire, so the shell takes the veto path instead of running the job.
    /// </summary>
    private sealed class VetoingTriggerListener : ITriggerListener
    {
        public string Name => "vetoing";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(true);
        }
    }

    public sealed class SucceedingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class ThrowingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("this job fails on purpose");
        }
    }

    /// <summary>
    /// A job reporting its own failure the way Quartz asks a job to, rather than letting an exception of
    /// its own out.
    /// </summary>
    public sealed class DeliberatelyFailingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new JobExecutionException("this job reports its own failure");
        }
    }
}
