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

using FluentAssertions;

using NUnit.Framework;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

[NonParallelizable]
public sealed class JobStoreActivityTracerTest : IDisposable
{
    private readonly List<Activity> startedActivities = new();
    private readonly List<Activity> stoppedActivities = new();
    private readonly ActivityListener activityListener;

    public JobStoreActivityTracerTest()
    {
        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivityOptions.DefaultListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => startedActivities.Add(activity),
            ActivityStopped = activity => stoppedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);
    }

    public void Dispose()
    {
        activityListener.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        startedActivities.Clear();
        stoppedActivities.Clear();
    }

    [Test]
    public async Task Trace_StartsAndStopsActivity()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("TestScheduler", "test-id-1");

        int result = await tracer.Trace(
            OperationName.JobStore.AcquireNextTriggers,
            () => new ValueTask<int>(42),
            activity => activity.SetTag(ActivityOptions.BatchSize, 10));

        result.Should().Be(42);

        startedActivities.Should().ContainSingle(a => a.OperationName == OperationName.JobStore.AcquireNextTriggers);
        stoppedActivities.Should().ContainSingle(a => a.OperationName == OperationName.JobStore.AcquireNextTriggers);
    }

    [Test]
    public async Task Trace_SetsSchedulerTags()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("MyScheduler", "sched-42");

        Activity capturedActivity = null;
        await tracer.Trace(
            OperationName.JobStore.StoreJob,
            () =>
            {
                capturedActivity = Activity.Current;
                return new ValueTask<bool>(true);
            });

        capturedActivity.Should().NotBeNull();
        capturedActivity.GetTagItem(ActivityOptions.SchedulerName).Should().Be("MyScheduler");
        capturedActivity.GetTagItem(ActivityOptions.SchedulerId).Should().Be("sched-42");
    }

    [Test]
    public async Task Trace_SetsNumericTagsNatively()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("Test", "1");

        Activity capturedActivity = null;
        await tracer.Trace(
            OperationName.JobStore.TriggersFired,
            () =>
            {
                capturedActivity = Activity.Current;
                return new ValueTask<bool>(true);
            },
            activity => activity.SetTag(ActivityOptions.TriggerCount, 5));

        capturedActivity.Should().NotBeNull();
        capturedActivity.GetTagItem(ActivityOptions.TriggerCount).Should().Be(5);
    }

    [Test]
    public async Task Trace_VoidOverload_StartsAndStopsActivity()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("Test", "1");

        bool executed = false;
        await tracer.Trace(
            OperationName.JobStore.PauseAll,
            () =>
            {
                executed = true;
                return ValueTask.CompletedTask;
            });

        executed.Should().BeTrue();
        startedActivities.Should().ContainSingle(a => a.OperationName == OperationName.JobStore.PauseAll);
        stoppedActivities.Should().ContainSingle(a => a.OperationName == OperationName.JobStore.PauseAll);
    }

    [Test]
    public async Task Trace_SetsErrorStatusOnException()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("Test", "1");

        Func<Task> act = async () => await tracer.Trace<int>(
            OperationName.JobStore.RemoveJob,
            () => throw new InvalidOperationException("test error"));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();

        var activity = stoppedActivities.Single(a => a.OperationName == OperationName.JobStore.RemoveJob);
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("test error");
        activity.Events.Should().ContainSingle(e => e.Name == "exception");
    }

    [Test]
    public async Task Trace_VoidOverload_SetsErrorStatusOnException()
    {
        var tracer = new JobStoreActivityTracer();
        tracer.SetSchedulerContext("Test", "1");

        Func<Task> act = async () => await tracer.Trace(
            OperationName.JobStore.ClearAllSchedulingData,
            new Func<ValueTask>(() => throw new InvalidOperationException("test error")));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();

        var activity = stoppedActivities.Single(a => a.OperationName == OperationName.JobStore.ClearAllSchedulingData);
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Test]
    public async Task Trace_FastPath_NoActivityWhenNoListener()
    {
        // Use a separate ActivitySource with no listener registered
        using var isolatedSource = new ActivitySource("Quartz.Test.Isolated");
        var tracer = new JobStoreActivityTracer(isolatedSource);
        tracer.SetSchedulerContext("Test", "1");

        int result = await tracer.Trace(
            OperationName.JobStore.AcquireNextTriggers,
            () => new ValueTask<int>(42));

        result.Should().Be(42);
        startedActivities.Should().NotContain(a => a.OperationName == OperationName.JobStore.AcquireNextTriggers);
    }

    [Test]
    public async Task Trace_VoidFastPath_NoActivityWhenNoListener()
    {
        using var isolatedSource = new ActivitySource("Quartz.Test.Isolated2");
        var tracer = new JobStoreActivityTracer(isolatedSource);
        tracer.SetSchedulerContext("Test", "1");

        bool executed = false;
        await tracer.Trace(
            OperationName.JobStore.PauseAll,
            () =>
            {
                executed = true;
                return ValueTask.CompletedTask;
            });

        executed.Should().BeTrue();
        startedActivities.Should().NotContain(a => a.OperationName == OperationName.JobStore.PauseAll);
    }
}
