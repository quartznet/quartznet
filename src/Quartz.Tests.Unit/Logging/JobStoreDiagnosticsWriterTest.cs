#if DIAGNOSTICS_SOURCE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Logging;

namespace Quartz.Tests.Unit.Logging;

/// <remarks>
/// DiagnosticListener.StartActivity/StopActivity write events named
/// "operationName.Start" / "operationName.Stop". We capture those via
/// an IObserver subscription rather than ActivityListener (which only
/// works with ActivitySource-created activities).
/// </remarks>
[NonParallelizable]
public sealed class JobStoreDiagnosticsWriterTest : IDisposable
{
    private readonly List<KeyValuePair<string, object>> events = new();
    private readonly IDisposable subscription;

    public JobStoreDiagnosticsWriterTest()
    {
        subscription = LogContext.Cached.Default.Value.Subscribe(new TestObserver(events));
    }

    public void Dispose()
    {
        subscription.Dispose();
    }

    [Test]
    public async Task Trace_StartsAndStopsActivity()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("TestScheduler", "test-id-1");

        int result = await writer.Trace(
            OperationName.JobStore.AcquireNextTriggers,
            () => Task.FromResult(42),
            activity => activity.AddTag(DiagnosticHeaders.BatchSize, "10")).ConfigureAwait(false);

        result.Should().Be(42);

        events.Should().Contain(e => e.Key == OperationName.JobStore.AcquireNextTriggers + ".Start");
        events.Should().Contain(e => e.Key == OperationName.JobStore.AcquireNextTriggers + ".Stop");
    }

    [Test]
    public async Task Trace_SetsSchedulerTags()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("MyScheduler", "sched-42");

        Activity capturedActivity = null;
        await writer.Trace(
            OperationName.JobStore.StoreJob,
            () =>
            {
                capturedActivity = Activity.Current;
                return Task.FromResult(true);
            }).ConfigureAwait(false);

        capturedActivity.Should().NotBeNull();
        capturedActivity.Tags.Should().Contain(t => t.Key == DiagnosticHeaders.SchedulerName && t.Value == "MyScheduler");
        capturedActivity.Tags.Should().Contain(t => t.Key == DiagnosticHeaders.SchedulerId && t.Value == "sched-42");
    }

    [Test]
    public async Task Trace_SetsCustomTags()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("Test", "1");

        Activity capturedActivity = null;
        await writer.Trace(
            OperationName.JobStore.TriggersFired,
            () =>
            {
                capturedActivity = Activity.Current;
                return Task.FromResult(true);
            },
            activity => activity.AddTag(DiagnosticHeaders.TriggerCount, "5")).ConfigureAwait(false);

        capturedActivity.Should().NotBeNull();
        capturedActivity.Tags.Should().Contain(t => t.Key == DiagnosticHeaders.TriggerCount && t.Value == "5");
    }

    [Test]
    public async Task Trace_VoidOverload_StartsAndStopsActivity()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("Test", "1");

        bool executed = false;
        await writer.Trace(
            OperationName.JobStore.PauseAll,
            () =>
            {
                executed = true;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

        executed.Should().BeTrue();
        events.Should().Contain(e => e.Key == OperationName.JobStore.PauseAll + ".Start");
        events.Should().Contain(e => e.Key == OperationName.JobStore.PauseAll + ".Stop");
    }

    [Test]
    public async Task Trace_WritesExceptionEvent_OnFailure()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("Test", "1");

        Func<Task> act = () => writer.Trace<int>(
            OperationName.JobStore.RemoveJob,
            () => throw new InvalidOperationException("test error"));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().ConfigureAwait(false);

        events.Should().Contain(e => e.Key == OperationName.JobStore.RemoveJob + ".Stop");
        events.Should().Contain(e => e.Key == OperationName.JobStore.RemoveJob + ".Exception");
    }

    [Test]
    public async Task Trace_VoidOverload_WritesExceptionEvent_OnFailure()
    {
        var writer = new JobStoreDiagnosticsWriter();
        writer.SetSchedulerContext("Test", "1");

        Func<Task> act = () => writer.Trace(
            OperationName.JobStore.ClearAllSchedulingData,
            new Func<Task>(() => throw new InvalidOperationException("test error")));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().ConfigureAwait(false);

        events.Should().Contain(e => e.Key == OperationName.JobStore.ClearAllSchedulingData + ".Stop");
        events.Should().Contain(e => e.Key == OperationName.JobStore.ClearAllSchedulingData + ".Exception");
    }

    private sealed class TestObserver : IObserver<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> events;

        public TestObserver(List<KeyValuePair<string, object>> events)
        {
            this.events = events;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(KeyValuePair<string, object> value) => events.Add(value);
    }
}

#endif
