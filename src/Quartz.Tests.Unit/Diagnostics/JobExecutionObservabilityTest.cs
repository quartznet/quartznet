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
/// Metric configuration is process-wide and idempotent, so within a whole-suite run no test can tell
/// which construction path configured it; what these tests do assert is that a scheduler built the way
/// nearly every application builds one emits, which fails the moment configuration is left out of that
/// path again. Run this fixture on its own — <c>--filter FullyQualifiedName~JobExecutionObservability</c>
/// — and nothing else in the process has built a scheduler, so <c>AddQuartz</c> is the only thing that
/// could have configured the meter.
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
    private const string ExceptionType = "scheduling.quartz.exception_type";

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
                if (instrument.Meter.Name == InstrumentationOptions.MeterName)
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
            ShouldListenTo = static source => source.Name == ActivityOptions.DefaultListenerName,
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

        published.Select(m => m.Meter).Should().AllBe(InstrumentationOptions.MeterName,
            "the meter name is the contract: it is what an exporter is told to subscribe to");

        published.Should().ContainSingle(m => m.Instrument == ExecuteCount)
            .Which.Value.Should().Be(1, "one execution counts once");

        published.Should().ContainSingle(m => m.Instrument == ExecuteDuration)
            .Which.Value.Should().BeGreaterThanOrEqualTo(0, "the duration is recorded in milliseconds");

        // Both measurements are asserted from the listener's point of view, which sees every one of them.
        // The instrument is a Counter rather than an UpDownCounter, so the -1 is a negative measurement on
        // an instrument OpenTelemetry treats as monotonic, and an aggregating exporter may not honour it.
        published.Where(m => m.Instrument == ExecuteActive).Should().HaveCount(2,
            "the in-progress count is incremented when the job starts and decremented when it ends");
        published.Where(m => m.Instrument == ExecuteActive).Sum(m => m.Value).Should().Be(0,
            "a finished job leaves nothing in progress");

        published.Should().NotContain(m => m.Instrument == ExecuteErrors,
            "the job succeeded");

        foreach (RecordedMeasurement measurement in published)
        {
            measurement.Tags.Should().Contain(new KeyValuePair<string, object>(ActivityOptions.TriggerGroup, execution.TriggerKey.Group))
                .And.Contain(new KeyValuePair<string, object>(ActivityOptions.TriggerName, execution.TriggerKey.Name))
                .And.Contain(new KeyValuePair<string, object>(ActivityOptions.JobGroup, execution.JobKey.Group))
                .And.Contain(new KeyValuePair<string, object>(ActivityOptions.JobName, execution.JobKey.Name));

            measurement.Tags.Should().HaveCount(4,
                "an execution is identified by its trigger and its job, and nothing else is added to it");
        }
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

        published.Where(m => m.Instrument == ExecuteActive).Sum(m => m.Value).Should().Be(0,
            "a job that throws leaves nothing in progress either — the decrement is not on the happy path only");

        published.Should().ContainSingle(m => m.Instrument == ExecuteDuration,
            "how long a job ran is worth knowing whether or not it failed");
    }

    /// <summary>
    /// The tag that says what went wrong is lost before it reaches the counter.
    /// </summary>
    /// <remarks>
    /// <c>Instrumentation.EndJobExecute</c> adds the exception type to <c>_tagList.Value</c>, and
    /// <see cref="Nullable{T}.Value"/> hands back a copy of the struct: the tag is added to a temporary
    /// that is thrown away on the next line, where a second copy — still four tags — is what the counter
    /// is given. So an exporter can see that executions failed but never what failed, which is most of
    /// the reason to look. This test states today's behavior so the loss is recorded rather than assumed;
    /// fixing <c>Instrumentation</c> to hold a mutable copy is a product change, and this test flips with
    /// it.
    /// </remarks>
    [Test]
    public async Task FailingJobExecution_LosesTheExceptionTypeTag()
    {
        Execution execution = await RunJob<ThrowingJob>();

        RecordedMeasurement error = MeasurementsFor(execution.JobKey).Single(m => m.Instrument == ExecuteErrors);

        error.Tags.Should().NotContainKey(ExceptionType,
            "the tag is added to a copy of the tag list and never reaches the counter — when that is fixed, "
            + "this expectation becomes a positive one for JobExecutionException");
    }

    [Test]
    public async Task JobExecution_EmitsActivityWithJobAndTriggerTags()
    {
        Execution execution = await RunJob<SucceedingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.OperationName.Should().Be(OperationName.Job.Execute);
        activity.Source.Name.Should().Be(ActivityOptions.DefaultListenerName,
            "the source name is what a tracer is told to subscribe to");
        activity.Kind.Should().Be(ActivityKind.Internal);
        activity.Status.Should().Be(ActivityStatusCode.Unset, "the job succeeded");

        activity.GetTagItem(ActivityOptions.SchedulerName).Should().Be(execution.SchedulerName);
        activity.GetTagItem(ActivityOptions.SchedulerId).Should().Be(execution.SchedulerInstanceId);
        activity.GetTagItem(ActivityOptions.JobType).Should().Be(new JobType(typeof(SucceedingJob)).FullName,
            "the job type is reported the way the scheduler names it — assembly-qualified, without a version");
        activity.GetTagItem(ActivityOptions.FireInstanceId).Should().Be(execution.FireInstanceId);
        activity.GetTagItem(ActivityOptions.TriggerGroup).Should().Be(execution.TriggerKey.Group);
        activity.GetTagItem(ActivityOptions.TriggerName).Should().Be(execution.TriggerKey.Name);
        activity.GetTagItem(ActivityOptions.JobGroup).Should().Be(execution.JobKey.Group);
        activity.GetTagItem(ActivityOptions.JobName).Should().Be(execution.JobKey.Name);
    }

    [Test]
    public async Task FailingJobExecution_EmitsActivityWithErrorStatusAndException()
    {
        Execution execution = await RunJob<ThrowingJob>();

        Activity activity = ActivityFor(execution.JobKey);

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.Events.Should().ContainSingle(e => e.Name == "exception",
            "the exception the job threw is recorded on the span that failed");
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
            measurements.Add(new RecordedMeasurement(instrument.Meter.Name, instrument.Name, value, copy));
        }
    }

    private List<RecordedMeasurement> MeasurementsFor(JobKey jobKey)
    {
        lock (measurements)
        {
            return measurements
                .Where(m => Equals(m.Tags.GetValueOrDefault(ActivityOptions.JobName), jobKey.Name))
                .ToList();
        }
    }

    private Activity ActivityFor(JobKey jobKey)
    {
        lock (stoppedActivities)
        {
            return stoppedActivities.Should().ContainSingle(a =>
                    a.OperationName == OperationName.Job.Execute
                    && Equals(a.GetTagItem(ActivityOptions.JobName), jobKey.Name))
                .Subject;
        }
    }

    private sealed record RecordedMeasurement(
        string Meter,
        string Instrument,
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
}
