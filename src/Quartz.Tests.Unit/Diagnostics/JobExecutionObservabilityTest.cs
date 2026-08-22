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
    private const string ExecuteCount = "scheduling.quartz.execute";
    private const string ExecuteErrors = "scheduling.quartz.execute.errors";
    private const string ExecuteActive = "scheduling.quartz.execute.active";
    private const string ExecuteDuration = "scheduling.quartz.execute.duration";
    // OpenTelemetry's conventional attribute for what an operation failed with, spelled out here rather
    // than read from Quartz, because the wire name is the contract an exporter and a dashboard are
    // written against.
    private const string ErrorTypeTag = "error.type";

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

        published.Should().ContainSingle(m => m.Instrument == ExecuteCount)
            .Which.Value.Should().Be(1, "one execution counts once");

        published.Should().ContainSingle(m => m.Instrument == ExecuteDuration)
            .Which.Value.Should().BeGreaterThanOrEqualTo(0, "the duration is recorded in milliseconds");

        List<RecordedMeasurement> active = published.Where(m => m.Instrument == ExecuteActive).ToList();

        active.Should().HaveCount(2,
            "the in-progress count is incremented when the job starts and decremented when it ends");
        active.Sum(m => m.Value).Should().Be(0, "a finished job leaves nothing in progress");
        active.Should().AllSatisfy(m => m.InstrumentType.Should().Be<UpDownCounter<long>>(
            "a count of what is running goes down as often as it goes up, and an exporter aggregates a "
            + "Counter as monotonic — which is what made the decrement something it could drop"));

        published.Should().NotContain(m => m.Instrument == ExecuteErrors,
            "the job succeeded");

        foreach (RecordedMeasurement measurement in published)
        {
            measurement.Tags.Should().Contain(new KeyValuePair<string, object>(ActivityTags.SchedulerName, execution.SchedulerName))
                .And.Contain(new KeyValuePair<string, object>(ActivityTags.TriggerGroup, execution.TriggerKey.Group))
                .And.Contain(new KeyValuePair<string, object>(ActivityTags.TriggerName, execution.TriggerKey.Name))
                .And.Contain(new KeyValuePair<string, object>(ActivityTags.JobGroup, execution.JobKey.Group))
                .And.Contain(new KeyValuePair<string, object>(ActivityTags.JobName, execution.JobKey.Name));

            measurement.Tags.Should().HaveCount(5,
                "an execution is identified by the scheduler that ran it, its trigger and its job, and "
                + "nothing else is added to it");
        }
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

        using MetricCollector<long> collector = new(
            provider.GetRequiredService<IMeterFactory>(),
            QuartzInstrumentation.MeterName,
            ExecuteCount);

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

        CollectedMeasurement<long> measurement = collector.LastMeasurement;
        measurement.Should().NotBeNull("the container's meter factory published this execution");
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().ContainKey(ActivityTags.SchedulerName)
            .WhoseValue.Should().Be(schedulerName,
                "a process can run several schedulers, and without the name their measurements are one "
                + "series a dashboard cannot separate again");
    }

    [Test]
    public async Task FailingJobExecution_PublishesErrorMetric()
    {
        Execution execution = await RunJob<ThrowingJob>();

        List<RecordedMeasurement> published = MeasurementsFor(execution.JobKey);

        published.Should().ContainSingle(m => m.Instrument == ExecuteCount)
            .Which.Value.Should().Be(1, "a job that throws still executed");

        published.Should().ContainSingle(m => m.Instrument == ExecuteErrors)
            .Which.Value.Should().Be(1, "a job that throws is one execution error");

        List<RecordedMeasurement> active = published.Where(m => m.Instrument == ExecuteActive).ToList();

        active.Sum(m => m.Value).Should().Be(0,
            "a job that throws leaves nothing in progress either — the decrement is not on the happy path only");
        active.Should().AllSatisfy(m => m.Tags.Should().NotContainKey(ErrorTypeTag,
            "an up-down counter is aggregated per attribute set, so the decrement has to carry exactly the "
            + "tags the increment carried or the series never comes back to zero"));

        published.Should().ContainSingle(m => m.Instrument == ExecuteDuration,
            "how long a job ran is worth knowing whether or not it failed")
            .Which.Tags.Should().ContainKey(ErrorTypeTag,
                "and a failed run's duration is worth telling apart from a successful one's");
    }

    /// <summary>
    /// The errors counter says what went wrong, not only that something did.
    /// </summary>
    /// <remarks>
    /// The tag used to be added to <c>_tagList.Value</c>, and <see cref="Nullable{T}.Value"/> hands back a
    /// copy of the struct: it went onto a temporary that was thrown away on the next line, where a second
    /// copy — still the four identity tags — was what the counter was given. Once it did arrive it named
    /// the <see cref="JobExecutionException"/> the run shell wraps anything a job throws in, which is the
    /// same answer for nearly every failure there is; what it reports now is the exception the job threw,
    /// found by unwrapping that pair of wrappers.
    /// </remarks>
    [Test]
    public async Task FailingJobExecution_TagsTheErrorWithTheExceptionTheJobThrew()
    {
        Execution execution = await RunJob<ThrowingJob>();

        RecordedMeasurement error = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteErrors);

        error.Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(InvalidOperationException).FullName,
                "the run shell wraps what a job throws as JobExecutionException -> "
                + "JobExecutionProcessException -> the cause, and naming either wrapper would tell an "
                + "exporter only that Quartz caught something, which it already knew from the counter");
    }

    /// <summary>
    /// Unwrapping stops at what the job threw, so a job that raises a <see cref="JobExecutionException"/>
    /// deliberately is reported as having thrown one.
    /// </summary>
    [Test]
    public async Task JobThrowingJobExecutionException_TagsTheErrorWithThatType()
    {
        Execution execution = await RunJob<DeliberatelyFailingJob>();

        RecordedMeasurement error = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteErrors);

        error.Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(JobExecutionException).FullName,
                "a JobExecutionException a job raised itself is not a wrapper the run shell added — there "
                + "is no JobExecutionProcessException under it — and it is what the job chose to say");

        ActivityFor(execution.JobKey).GetTagItem(ErrorTypeTag).Should().Be(typeof(JobExecutionException).FullName,
            "the span and the errors counter answer the same question the same way");
    }

    [Test]
    public async Task JobExecution_EmitsActivityWithJobAndTriggerTags()
    {
        Execution execution = await RunJob<SucceedingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.OperationName.Should().Be(OperationName.Job.Execute);
        activity.Source.Name.Should().Be(QuartzInstrumentation.ActivitySourceName,
            "the source name is what a tracer is told to subscribe to");
        activity.Kind.Should().Be(ActivityKind.Internal);
        activity.Status.Should().Be(ActivityStatusCode.Unset, "the job succeeded");

        activity.GetTagItem(ActivityTags.SchedulerName).Should().Be(execution.SchedulerName);
        activity.GetTagItem(ActivityTags.SchedulerId).Should().Be(execution.SchedulerInstanceId);
        activity.GetTagItem(ActivityTags.JobType).Should().Be(new JobType(typeof(SucceedingJob)).FullName,
            "the job type is reported the way the scheduler names it — assembly-qualified, without a version");
        activity.GetTagItem(ActivityTags.FireInstanceId).Should().Be(execution.FireInstanceId);
        activity.GetTagItem(ActivityTags.TriggerGroup).Should().Be(execution.TriggerKey.Group);
        activity.GetTagItem(ActivityTags.TriggerName).Should().Be(execution.TriggerKey.Name);
        activity.GetTagItem(ActivityTags.JobGroup).Should().Be(execution.JobKey.Group);
        activity.GetTagItem(ActivityTags.JobName).Should().Be(execution.JobKey.Name);
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
            "the span classifies the failure with the attribute the errors counter is tagged with, so one "
            + "value finds the failed executions in a trace and in a metric alike");

        RecordedMeasurement error = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteErrors);
        error.Tags[ErrorTypeTag].Should().Be(activity.GetTagItem(ErrorTypeTag),
            "the two signals are read together, and disagreeing about what failed is worse than either "
            + "of them being silent");
    }

    /// <summary>
    /// Builds a scheduler the way an application does, runs the job its trigger fires exactly once, and
    /// shuts everything down before returning — so an assertion never races the execution it is about.
    /// </summary>
    private static async Task<Execution> RunJob<TJob>() where TJob : IJob
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
            quartz.ConfigureScheduler(options => options.InstanceName = $"observability-{id}");
            quartz.AddJobListener(completion);
            quartz.AddJob<TJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<TJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity(triggerKey)
                .StartNow());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            await scheduler.Start();

            Task finished = await Task.WhenAny(completion.Executed, Task.Delay(TimeSpan.FromSeconds(30)));
            finished.Should().BeSameAs(completion.Executed, "the scheduled job should have run");

            return new Execution(
                jobKey,
                triggerKey,
                scheduler.SchedulerName,
                scheduler.SchedulerInstanceId,
                await completion.Executed);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
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
            // a Counter is a monotonic sum, an UpDownCounter is not.
            measurements.Add(new RecordedMeasurement(instrument.Meter.Name, instrument.Name, instrument.GetType(), value, copy));
        }
    }

    private List<RecordedMeasurement> MeasurementsFor(JobKey jobKey)
    {
        lock (measurements)
        {
            return measurements
                .Where(m => Equals(m.Tags.GetValueOrDefault(ActivityTags.JobName), jobKey.Name))
                .ToList();
        }
    }

    private Activity ActivityFor(JobKey jobKey)
    {
        lock (stoppedActivities)
        {
            return stoppedActivities.Should().ContainSingle(a =>
                    a.OperationName == OperationName.Job.Execute
                    && Equals(a.GetTagItem(ActivityTags.JobName), jobKey.Name))
                .Subject;
        }
    }

    private sealed record RecordedMeasurement(
        string Meter,
        string Instrument,
        Type InstrumentType,
        double Value,
        Dictionary<string, object> Tags);

    private sealed record Execution(
        JobKey JobKey,
        TriggerKey TriggerKey,
        string SchedulerName,
        string SchedulerInstanceId,
        string FireInstanceId);

    /// <summary>
    /// Signals once the shell has finished with the execution, which is after both the activity and the
    /// meter measurements have been closed out.
    /// </summary>
    private sealed class ExecutionCompletionListener : IJobListener
    {
        private readonly TaskCompletionSource<string> executed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "execution-completion";

        public Task<string> Executed => executed.Task;

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            executed.TrySetResult(context.FireInstanceId);
            return default;
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
