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

using System.Threading.Channels;

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// How a running job's progress reaches the store: at most once a second, only on a change, the last
/// value always, and never at the job's expense.
/// </summary>
/// <remarks>
/// The clock is a <see cref="FakeTimeProvider" />, so "a second later" is a call rather than a wait. The
/// store's writes land on the thread pool, which is why the counts are read off the writer — it counts
/// a write when it queues one — and the values off what the store was handed.
/// </remarks>
public sealed class FireProgressWriterTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(10);

    private FakeTimeProvider clock = null!;
    private IJobStore store = null!;
    private Channel<(string FireInstanceId, FireInstanceProgress Progress)> writes = null!;
    private FakeLogger logger = null!;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        writes = Channel.CreateUnbounded<(string, FireInstanceProgress)>();
        logger = new FakeLogger();

        store = A.Fake<IJobStore>();
        A.CallTo(() => store.UpdateFireInstanceProgress(A<string>._, A<FireInstanceProgress>._, A<CancellationToken>._))
            .ReturnsLazily((string id, FireInstanceProgress progress, CancellationToken _) =>
            {
                writes.Writer.TryWrite((id, progress));
                return default;
            });
    }

    [Test]
    public async Task TheFirstReportIsWrittenAtOnce()
    {
        JobExecutionContextImpl context = Context();
        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        context.ReportProgress(10, "starting");

        writer.WritesStarted.Should().Be(1, "nothing has been written for this firing, so nothing makes the first report wait");

        (string fireInstanceId, FireInstanceProgress progress) = await NextWrite();
        fireInstanceId.Should().Be("fire-1", "the write names the firing that reported");
        progress.Percent.Should().Be(10);
        progress.Message.Should().Be("starting");
    }

    [Test]
    public async Task ReportsInsideTheIntervalWaitForItAndTheLastOneIsWritten()
    {
        JobExecutionContextImpl context = Context();
        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        context.ReportProgress(10);
        await NextWrite();
        await Idle(writer);

        context.ReportProgress(20);
        context.ReportProgress(30, "most of the way");
        context.ReportProgress(40, "nearly");

        writer.WritesStarted.Should().Be(1,
            "a second has not passed since the first write, so the three reports are kept rather than written");

        clock.Advance(TimeSpan.FromMilliseconds(999));
        writer.WritesStarted.Should().Be(1, "and it still has not");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        writer.WritesStarted.Should().Be(2, "the interval is up, and the value that waited for it is written");

        (_, FireInstanceProgress progress) = await NextWrite();
        progress.Percent.Should().Be(40,
            "the last report is the one that goes, however many came before it inside the interval");
        progress.Message.Should().Be("nearly");
    }

    [Test]
    public async Task AReportThatChangesNothingIsNotWritten()
    {
        JobExecutionContextImpl context = Context();
        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        context.ReportProgress(50, "halfway");
        await NextWrite();
        await Idle(writer);

        clock.Advance(TimeSpan.FromSeconds(5));
        context.ReportProgress(50, "halfway");
        clock.Advance(TimeSpan.FromSeconds(5));

        writer.WritesStarted.Should().Be(1,
            "the store already holds 50% and 'halfway', so writing them again would be a round trip that says nothing");

        context.ReportProgress(50, "halfway, still");
        writer.WritesStarted.Should().Be(2, "a new message is a change even when the percentage is not");
    }

    [Test]
    public async Task AReportAfterTheIntervalIsWrittenAtOnce()
    {
        JobExecutionContextImpl context = Context();
        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        context.ReportProgress(10);
        await NextWrite();
        await Idle(writer);

        clock.Advance(TimeSpan.FromSeconds(3));
        context.ReportProgress(70);

        writer.WritesStarted.Should().Be(2, "the interval has long passed, so there is nothing to wait for");
        (await NextWrite()).Progress.Percent.Should().Be(70);
    }

    [Test]
    public async Task AStoreThatFailsIsLoggedAndTheJobCarriesOn()
    {
        A.CallTo(() => store.UpdateFireInstanceProgress(A<string>._, A<FireInstanceProgress>._, A<CancellationToken>._))
            .Throws(new JobPersistenceException("the database went away"));

        JobExecutionContextImpl context = Context();
        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        Action report = () => context.ReportProgress(10, "starting");
        report.Should().NotThrow("a progress report is a record of what the job said, and failing to keep it is no reason to fail the job");

        await Idle(writer);

        FakeLogRecord record = logger.Collector.GetSnapshot().Should().ContainSingle().Which;
        record.Level.Should().Be(LogLevel.Warning);
        record.Id.Id.Should().Be(1058, "the event id is what an operator filters and alerts on");
        record.Message.Should().Contain("fire-1").And.Contain("jobGroup.jobName");
        record.Exception.Should().BeOfType<JobPersistenceException>();

        clock.Advance(TimeSpan.FromSeconds(1));
        context.ReportProgress(20);
        writer.WritesStarted.Should().Be(2, "a failed write is forgotten, and the next change is tried like any other");
    }

    [Test]
    public async Task NothingIsWrittenOnceTheFiringIsOver()
    {
        JobExecutionContextImpl context = Context();

        // Reported from inside the firing, as a job does: the holder is the one the run shell empties.
        AmbientJobExecution.Holder holder = AmbientJobExecution.Begin(Guid.NewGuid());
        IDisposable firing = holder.Enter(context);

        FireProgressWriter writer = FireProgressWriter.Attach(context, store, clock, logger);

        context.ReportProgress(10);
        await NextWrite();
        await Idle(writer);

        context.ReportProgress(90, "almost done");
        firing.Dispose();

        clock.Advance(TimeSpan.FromSeconds(1));

        writer.WritesStarted.Should().Be(1,
            "the fire instance went with the firing, and a write made after it would only reach a store that may be shutting down");
    }

    [Test]
    public void ProgressOutsideZeroToOneHundredIsRefused()
    {
        JobExecutionContextImpl context = Context();

        Action below = () => context.ReportProgress(-1);
        Action above = () => context.ReportProgress(101);

        below.Should().Throw<ArgumentOutOfRangeException>("a percentage below zero is a bug in the job, not a value to store");
        above.Should().Throw<ArgumentOutOfRangeException>("and so is one above a hundred");
    }

    [Test]
    public void AContextOverASchedulerQuartzDidNotBuildKeepsTheValueAndWritesNothing()
    {
        JobExecutionContextImpl context = Context();

        context.ReportProgress(25, "a quarter");
        context.ReportProgress(35, "a third, near enough");

        FireProgressWriter? writer = FireProgressWriter.Find(context);

        writer.Should().NotBeNull("the first report is what makes the writer");
        writer!.Latest.Should().Be((35, "a third, near enough"),
            "the latest value is kept whether or not there is a store to write it to");
        writer.WritesStarted.Should().Be(0, "and there is no store here to write it to");
    }

    [Test]
    public void AContextThatNeverReportsHasNoWriter()
    {
        JobExecutionContextImpl context = Context();

        FireProgressWriter.Find(context).Should().BeNull(
            "a firing that never reports must cost nothing, and a writer made for it would be a cost");
    }

    [Test]
    public void ALongMessageIsCutAtTheColumnWidthAndNeverInsideASurrogatePair()
    {
        JobExecutionContextImpl context = Context();

        context.ReportProgress(1, new string('x', 300));
        FireProgressWriter.Find(context)!.Latest.Message.Should().HaveLength(FireInstanceProgress.MaxMessageLength,
            "the ADO.NET store's column is 250 wide, and every store is handed what that one can hold");

        string straddling = new string('x', FireInstanceProgress.MaxMessageLength - 1) + "\U0001F600" + "tail";
        context.ReportProgress(2, straddling);
        string kept = FireProgressWriter.Find(context)!.Latest.Message!;

        kept.Should().HaveLength(FireInstanceProgress.MaxMessageLength - 1,
            "cutting between the two halves of a surrogate pair would hand the store a string that is not valid UTF-16");
        char.IsHighSurrogate(kept[^1]).Should().BeFalse();
    }

    [Test]
    public void TheDefaultReportProgressDoesNothing()
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>(options => options.CallsBaseMethods());

        Action report = () => context.ReportProgress(50, "halfway");

        report.Should().NotThrow(
            "a context implemented outside Quartz compiles unchanged and reports nothing, which is what the default says");
    }

    [Test]
    public async Task TheDefaultUpdateFireInstanceProgressRecordsNothing()
    {
        IJobStore defaults = A.Fake<IJobStore>(options => options.CallsBaseMethods());

        Func<Task> update = async () => await defaults.UpdateFireInstanceProgress("fire-1", new FireInstanceProgress { Percent = 50 });

        await update.Should().NotThrowAsync(
            "a store written against an earlier 4.x keeps working, and its firings simply report no progress");
    }

    [Test]
    public async Task ADelegatingStoreHandsTheReportOn()
    {
        IJobStore inner = A.Fake<IJobStore>();
        DelegatingJobStore delegating = new(inner);
        FireInstanceProgress progress = new() { Percent = 60, Message = "sixty" };

        await delegating.UpdateFireInstanceProgress("fire-1", progress);

        A.CallTo(() => inner.UpdateFireInstanceProgress("fire-1", progress, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private static JobExecutionContextImpl Context()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        ((IOperableTrigger) bundle.Trigger).FireInstanceId = "fire-1";
        return new JobExecutionContextImpl(A.Fake<IScheduler>(), bundle, A.Fake<IJob>());
    }

    private async Task<(string FireInstanceId, FireInstanceProgress Progress)> NextWrite()
    {
        using CancellationTokenSource timeout = new(waitLimit);
        return await writes.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>
    /// Waits for the write in flight to finish, including the bookkeeping after the store call — which
    /// is where the next tick is armed, and so has to be over before the clock is moved.
    /// </summary>
    private static async Task Idle(FireProgressWriter writer)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (writer.Writing)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail("the progress write did not finish");
            }

            await Task.Delay(5);
        }
    }
}
