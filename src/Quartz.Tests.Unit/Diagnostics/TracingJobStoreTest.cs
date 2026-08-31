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
using System.Text;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The store spans and the store-operation histogram, which used to come from thirty-three call sites
/// inside the ADO store and now come from a decorator over whatever store a scheduler was given.
/// </summary>
/// <remarks>
/// The point of the move is that the spans no longer belong to one store: the in-memory store, the Redis
/// store and any store an application wrote all produce them now, and the ADO store produces exactly the
/// ones it always did. Both halves of that are asserted here.
/// </remarks>
[NonParallelizable]
public sealed class TracingJobStoreTest
{
    private const string OperationDuration = "quartz.jobstore.operation.duration";
    private const string SchedulerNameTag = "quartz.scheduler.name";
    private const string SchedulerIdTag = "quartz.scheduler.id";
    private const string OperationTag = "quartz.jobstore.operation";
    private const string ErrorTypeTag = "error.type";

    private readonly List<Activity> stoppedActivities = [];
    private readonly List<RecordedMeasurement> measurements = [];

    private ActivityListener activityListener;
    private MeterListener meterListener;

    [SetUp]
    public void SetUp()
    {
        lock (stoppedActivities)
        {
            stoppedActivities.Clear();
        }

        lock (measurements)
        {
            measurements.Clear();
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

        meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name != OperationDuration)
            {
                return;
            }

            Dictionary<string, object> copy = new(tags.Length, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (measurements)
            {
                measurements.Add(new RecordedMeasurement(instrument.Unit, value, copy));
            }
        });
        meterListener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        activityListener?.Dispose();
        meterListener?.Dispose();
    }

    /// <summary>
    /// The regression this whole change is about: the in-memory store produced no store spans at all,
    /// because tracing lived inside the ADO store.
    /// </summary>
    [Test]
    public async Task TheInMemoryStore_NowEmitsStoreSpans()
    {
        IJobStore store = await Decorated(TestJobStores.Ram());

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job", "jobs").Build();
        await store.AddJob(job);

        Activity span = SpanFor(OperationName.JobStore.AddJob);

        span.Kind.Should().Be(ActivityKind.Client,
            "a store call is an outbound request as far as a trace is concerned, whether the store is a "
            + "database or a dictionary");
        span.Source.Name.Should().Be(QuartzInstrumentation.ActivitySourceName);
        span.GetTagItem(SchedulerNameTag).Should().Be("traced");
        span.GetTagItem(SchedulerIdTag).Should().Be("node-1");
        span.GetTagItem(ActivityTags.JobGroup).Should().Be("jobs");
        span.GetTagItem(ActivityTags.JobName).Should().Be("job");
    }

    /// <summary>
    /// And the ADO store still emits exactly the names it did, since a dashboard was written against
    /// them. The snapshot is the whole set the decorator can produce.
    /// </summary>
    [Test]
    public async Task EveryTracedOperation_KeepsTheSpanNameItAlwaysHad()
    {
        IJobStore store = await Decorated(StubStore());

        await ExerciseEveryTracedOperation(store);

        List<string> names;
        lock (stoppedActivities)
        {
            names = stoppedActivities.Select(a => a.OperationName).Distinct().Order(StringComparer.Ordinal).ToList();
        }

        StringBuilder rendered = new();
        foreach (string name in names)
        {
            rendered.AppendLine(name);
        }

        await Verify(rendered.ToString(), extension: "txt")
            .UseDirectory("../Verify")
            .UseFileName("TracingJobStoreTest_SpanNames")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// The enrichment the ADO store used to add by hand, derived here from the arguments instead — so
    /// every store carries it rather than only the database one.
    /// </summary>
    [Test]
    public async Task AcquisitionSpan_CarriesTheBatchAskedForAndTheBatchReturned()
    {
        IJobStore inner = StubStore();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().ForJob("job", "jobs").StartNow().Build();

        A.CallTo(() => inner.AcquireNextTriggers(A<TriggerAcquisitionRequest>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(new List<IOperableTrigger> { trigger }));

        IJobStore store = await Decorated(inner);

        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UnixEpoch,
            MaxCount = 7,
            TimeWindow = TimeSpan.Zero,
        });

        Activity span = SpanFor(OperationName.JobStore.AcquireNextTriggers);

        span.GetTagItem(ActivityTags.BatchSize).Should().Be(7, "the batch the scheduler asked for");
        span.GetTagItem(ActivityTags.TriggerCount).Should().Be(1,
            "and how much of it the store could fill, which is only knowable once it has answered");
    }

    [Test]
    public async Task EveryOperation_IsTimedOnTheStoreOperationHistogram()
    {
        IJobStore store = await Decorated(TestJobStores.Ram());

        await store.PauseAll();

        RecordedMeasurement measurement = measurements.Should().ContainSingle().Subject;

        measurement.Unit.Should().Be("s", "OpenTelemetry records durations in seconds");
        measurement.Value.Should().BeGreaterThanOrEqualTo(0);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerNameTag, "traced"))
            .And.Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-1"))
            .And.Contain(new KeyValuePair<string, object>(OperationTag, OperationName.JobStore.PauseAll),
                "the measurement is named by the same string the operation's span is called, so one value "
                + "finds a slow operation in a trace and in a metric alike");

        measurement.Tags.Should().NotContainKey(ErrorTypeTag, "nothing failed");
    }

    [Test]
    public async Task AFailedOperation_EndsTheSpanInErrorAndTagsTheMeasurement()
    {
        IJobStore inner = StubStore();
        A.CallTo(() => inner.PauseAll(A<CancellationToken>.Ignored))
            .Throws(new JobPersistenceException("the database is gone"));

        IJobStore store = await Decorated(inner);

        Func<Task> act = async () => await store.PauseAll();
        await act.Should().ThrowAsync<JobPersistenceException>(
            "the decorator observes the failure and lets it through unchanged");

        Activity span = SpanFor(OperationName.JobStore.PauseAll);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.Events.Should().ContainSingle(e => e.Name == "exception");

        measurements.Should().ContainSingle()
            .Which.Tags.Should().ContainKey(ErrorTypeTag)
            .WhoseValue.Should().Be(typeof(JobPersistenceException).FullName,
                "a store that is failing is the thing an alert on this histogram is for, and it would "
                + "otherwise be indistinguishable from a fast one");
    }

    /// <summary>
    /// The cost when nobody is watching, which is what makes wrapping every store affordable.
    /// </summary>
    [Test]
    public async Task WithNoListener_NothingIsCreatedAndTheInnerCallIsReturnedDirectly()
    {
        // A source of its own so that this test's own ActivityListener, which listens to "Quartz", is
        // not what it measures.
        using ActivitySource unlistened = new("Quartz.Tests.Unlistened");
        IJobStore inner = StubStore();

        TracingJobStore store = new(inner, new Meters(meterFactory: null), TimeProvider.System, unlistened);
        await store.Initialize(new SchedulerIdentity { SchedulerName = "quiet", InstanceId = "node-0" });

        await store.PauseAll();

        lock (stoppedActivities)
        {
            stoppedActivities.Should().BeEmpty("no listener means no activity to create");
        }

        A.CallTo(() => inner.PauseAll(A<CancellationToken>.Ignored)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The identity hazard the decorator introduces, pinned at the level an application sees it.
    /// </summary>
    [Test]
    public async Task ADecoratedStore_StillReportsWhatTheSchedulerIsBuiltOn()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = "metadata-check"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        SchedulerMetadata metadata = await scheduler.GetMetadata();

        metadata.JobStoreTypeName.Should().Contain(nameof(RAMJobStore),
            "the type a scheduler reports is the store that keeps the data, not the tracing layer over "
            + "it — that name is what the dashboard shows and what the startup log line says");

        metadata.JobStorePersistent.Should().BeFalse(
            "persistence is a question about behaviour, which the decorator forwards");
        metadata.JobStoreClustered.Should().BeFalse();
    }

    /// <summary>
    /// And the enlistment check, which reads the scheduler's store to decide whether an ambient
    /// transaction can be honoured at all. Behind the decorator it must still find the ADO store.
    /// </summary>
    [Test]
    public void ADecoratedPersistentStore_IsStillFoundByTheEnlistmentCheck()
    {
        IJobStore decorated = new TracingJobStore(
            TestJobStores.Tx(),
            new Meters(meterFactory: null),
            TimeProvider.System);

        JobStores.Unwrap(decorated).Should().BeOfType<LocalTransactionJobStore>(
            "SchedulerEnlistmentExtensions refuses an enlistment when the store is not an AdoJobStoreBase, "
            + "and a bare type test on a wrapped store would refuse every enlistment there is");

        decorated.SupportsPersistence.Should().BeTrue();
    }

    private static IJobStore StubStore()
    {
        IJobStore store = A.Fake<IJobStore>();

        // The one member whose result the decorator reads. A fake's default is a null list, which is not
        // something any store returns.
        A.CallTo(() => store.AcquireNextTriggers(A<TriggerAcquisitionRequest>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<IOperableTrigger>>(new List<IOperableTrigger>()));

        return store;
    }

    private static async Task<IJobStore> Decorated(IJobStore inner)
    {
        SchedulerIdentity identity = new() { SchedulerName = "traced", InstanceId = "node-1" };
        await inner.Initialize(identity);

        TracingJobStore store = new(inner, new Meters(meterFactory: null), TimeProvider.System);
        await store.Initialize(identity);
        return store;
    }

    /// <summary>
    /// Calls every member the decorator traces, so the snapshot below is the complete set of names.
    /// </summary>
    private static async Task ExerciseEveryTracedOperation(IJobStore store)
    {
        JobKey jobKey = new("job", "jobs");
        TriggerKey triggerKey = new("trigger", "triggers");
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob(jobKey).WithIdentity(triggerKey).StartNow().Build();

        await store.ScheduleJob(job, trigger);
        await store.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>>());
        await store.AddJob(job, AddJobOptions.Replacing);
        await store.AddTrigger(trigger, AddTriggerOptions.Replacing);
        await store.AddCalendar("calendar", new Quartz.Impl.Calendar.BaseCalendar());
        await store.DeleteJob(jobKey);
        await store.DeleteJobs([jobKey]);
        await store.DeleteJobs(GroupMatcher<JobKey>.AnyGroup());
        await store.DeleteTrigger(triggerKey);
        await store.DeleteTriggers([triggerKey]);
        await store.DeleteTriggers(GroupMatcher<TriggerKey>.AnyGroup());
        await store.DeleteCalendar("calendar");
        await store.ReplaceTrigger(triggerKey, trigger);
        await store.UpdateTriggerDetails(triggerKey, new TriggerDetailsUpdate());
        await store.ResetTriggerFromErrorState(triggerKey);
        await store.ResetTriggersFromErrorState([triggerKey]);
        await store.PauseTrigger(triggerKey);
        await store.PauseTriggers(GroupMatcher<TriggerKey>.AnyGroup());
        await store.PauseTriggers([triggerKey]);
        await store.PauseJob(jobKey);
        await store.PauseJobs(GroupMatcher<JobKey>.AnyGroup());
        await store.PauseJobs([jobKey]);
        await store.ResumeTrigger(triggerKey);
        await store.ResumeTriggers(GroupMatcher<TriggerKey>.AnyGroup());
        await store.ResumeTriggers([triggerKey]);
        await store.ResumeJob(jobKey);
        await store.ResumeJobs(GroupMatcher<JobKey>.AnyGroup());
        await store.ResumeJobs([jobKey]);
        await store.PauseAll();
        await store.ResumeAll();
        await store.Clear();
        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UnixEpoch,
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        await store.ReleaseAcquiredTrigger(trigger);
        await store.TriggersFired([trigger]);
        await store.TriggeredJobComplete(trigger, job, SchedulerInstruction.NoInstruction);
    }

    private Activity SpanFor(string operationName)
    {
        lock (stoppedActivities)
        {
            return stoppedActivities.Should().ContainSingle(a => a.OperationName == operationName).Subject;
        }
    }

    private sealed record RecordedMeasurement(string Unit, double Value, Dictionary<string, object> Tags);

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
