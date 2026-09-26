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

using System.Text;

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The bounded buffer a captured firing's lines are kept in, and the logger that writes into it only on
/// that firing's own flow.
/// </summary>
public sealed class ExecutionLogCaptureTest
{
    private static readonly DateTimeOffset now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ALineIsKeptWithItsTimeLevelAndCategory()
    {
        ExecutionLogBuffer buffer = new(maxLines: 10, maxBytes: 4096, new FakeTimeProvider(now.AddMilliseconds(250)));

        buffer.Append(LogLevel.Information, "Reports.NightlyJob", "starting", exception: null);
        buffer.Append(LogLevel.Warning, "Reports.NightlyJob", "slow page", exception: null);

        buffer.ToText().Should().Be(
            "2030-01-01T12:00:00.250Z info Reports.NightlyJob: starting\n"
            + "2030-01-01T12:00:00.250Z warn Reports.NightlyJob: slow page",
            "a captured log reads like the console one: UTC time, the four-letter level, the category, the message");
    }

    [Test]
    public void AnExceptionIsKeptUnderTheLineThatCarriedIt()
    {
        ExecutionLogBuffer buffer = new(maxLines: 10, maxBytes: 8192, new FakeTimeProvider(now));

        buffer.Append(LogLevel.Error, "Reports.NightlyJob", "the export failed", new InvalidOperationException("the disk is full"));

        string text = buffer.ToText()!;
        text.Should().StartWith("2030-01-01T12:00:00.000Z fail Reports.NightlyJob: the export failed\n");
        text.Should().Contain("System.InvalidOperationException: the disk is full",
            "what the job threw is usually the most useful line of all");
    }

    [Test]
    public void EveryLevelHasItsName()
    {
        ExecutionLogBuffer buffer = new(maxLines: 10, maxBytes: 4096, new FakeTimeProvider(now));

        foreach (LogLevel level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Critical, LogLevel.None })
        {
            buffer.Append(level, "c", "m", exception: null);
        }

        buffer.ToText()!.Split('\n').Select(line => line.Split(' ')[1]).Should().Equal(["trce", "dbug", "crit", "none"]);
    }

    [Test]
    public void TheOldestLinesAreDroppedPastTheLineBound()
    {
        ExecutionLogBuffer buffer = new(maxLines: 3, maxBytes: 4096, new FakeTimeProvider(now));

        for (int i = 1; i <= 5; i++)
        {
            buffer.Append(LogLevel.Information, "Job", "line " + i, exception: null);
        }

        string[] lines = buffer.ToText()!.Split('\n');

        lines[0].Should().Be("[2 earlier log entries dropped: the capture keeps at most 3 entries and 4096 bytes]",
            "a log that silently starts in the middle reads as though the job began there");
        lines.Skip(1).Select(line => line[(line.LastIndexOf(':') + 2)..]).Should().Equal(["line 3", "line 4", "line 5"],
            "the end of a run is where a failure says what went wrong, so the newest lines are the ones kept");
        buffer.Dropped.Should().Be(2);
    }

    [Test]
    public void TheOldestLinesAreDroppedPastTheByteBound()
    {
        ExecutionLogBuffer buffer = new(maxLines: 100, maxBytes: 256, new FakeTimeProvider(now));

        // Each entry is 24 bytes of timestamp and level, the category and colon, and 70 of message: two of
        // them fit in 256 bytes and a third does not.
        for (int i = 1; i <= 4; i++)
        {
            buffer.Append(LogLevel.Information, "Job", i + new string('x', 69), exception: null);
        }

        string text = buffer.ToText()!;
        string kept = text[(text.IndexOf('\n') + 1)..];

        Encoding.UTF8.GetByteCount(kept).Should().BeLessThanOrEqualTo(256, "the bound is on what is kept, in UTF-8 bytes");
        kept.Split('\n').Should().HaveCount(2);
        kept.Should().Contain("3xxx").And.Contain("4xxx").And.NotContain("2xxx");
        buffer.Dropped.Should().Be(2);
    }

    [Test]
    public void AnEntryLargerThanTheWholeBoundIsCutToFit()
    {
        ExecutionLogBuffer buffer = new(maxLines: 10, maxBytes: 300, new FakeTimeProvider(now));

        buffer.Append(LogLevel.Information, "Job", new string('é', 1000), exception: null);

        string text = buffer.ToText()!;
        Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(300,
            "an entry on its own is still held to the bound, or one enormous line would carry the whole budget past it");
        text.Should().EndWith(" [cut]", "and it says it was cut rather than looking like the whole message");
        buffer.Dropped.Should().Be(0, "a cut entry is kept, not dropped");
    }

    [Test]
    public void AFiringThatLoggedNothingHasNoLog()
    {
        new ExecutionLogBuffer(maxLines: 10, maxBytes: 4096, new FakeTimeProvider(now)).ToText().Should().BeNull(
            "no log and an empty log are different facts, and the history row says the first");
    }

    [Test]
    public void TheLoggerWritesNothingOutsideAFiring()
    {
        ILogger logger = new ExecutionLogCaptureProvider().CreateLogger("Job");

        logger.IsEnabled(LogLevel.Information).Should().BeFalse(
            "outside a firing there is no buffer to write into, so nothing should be formatted for this provider");

        Action log = () => logger.LogInformation("nobody keeps this");
        log.Should().NotThrow();
    }

    [Test]
    public async Task TheLoggerWritesNothingIntoAFiringNobodyCaptures()
    {
        // Async so the ambient firing set here is undone when the test returns, rather than left on the
        // thread for the next one.
        await Task.Yield();

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        AmbientJobExecution.Holder holder = AmbientJobExecution.Begin(Guid.NewGuid());

        using (holder.Enter(context))
        {
            ILogger logger = new ExecutionLogCaptureProvider().CreateLogger("Job");

            logger.IsEnabled(LogLevel.Information).Should().BeFalse(
                "a firing of a scheduler that did not ask for capture has no buffer, so its lines are not kept");
            logger.LogInformation("not kept");

            ExecutionLogCapture.Find(context).Should().BeNull();
        }
    }

    [Test]
    public async Task TheLoggerWritesIntoTheBufferOfTheFiringItIsCalledOn()
    {
        // Async so the ambient firing set here is undone when the test returns, rather than left on the
        // thread for the next one.
        await Task.Yield();

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        AmbientJobExecution.Holder holder = AmbientJobExecution.Begin(Guid.NewGuid());
        ExecutionLogCapture.Begin(context, new ExecutionLogCaptureOptions(), new FakeTimeProvider(now));

        using (holder.Enter(context))
        {
            ILogger logger = new ExecutionLogCaptureProvider().CreateLogger("Reports.NightlyJob");

            logger.IsEnabled(LogLevel.Information).Should().BeTrue();
            logger.IsEnabled(LogLevel.None).Should().BeFalse();
            logger.BeginScope("a scope").Should().BeNull("scopes are not captured; the lines are");

            logger.LogInformation("page {Page} of {Pages}", 3, 10);
            logger.Log(LogLevel.None, "never written");
        }

        ExecutionLogCapture.Find(context)!.ToText().Should().Be(
            "2030-01-01T12:00:00.000Z info Reports.NightlyJob: page 3 of 10",
            "the line is formatted by the logging call's own formatter, and LogLevel.None is not a line");
    }

    [Test]
    public void BeginningTwiceKeepsTheFirstBuffer()
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        ExecutionLogCapture.Begin(context, new ExecutionLogCaptureOptions(), new FakeTimeProvider(now));

        ExecutionLogBuffer first = ExecutionLogCapture.Find(context)!;
        first.Append(LogLevel.Information, "Job", "before the refire", exception: null);

        ExecutionLogCapture.Begin(context, new ExecutionLogCaptureOptions(), new FakeTimeProvider(now));

        ExecutionLogCapture.Find(context).Should().BeSameAs(first,
            "a refire runs the pipeline again with the same context, and its lines belong with the first attempt's");
    }

    [TestCase(0, 16384, "MaxLines")]
    [TestCase(200, 255, "MaxBytes")]
    public void BoundsThatKeepNothingAreRefused(int maxLines, int maxBytes, string named)
    {
        ValidateOptionsResult result = new ExecutionLogCaptureOptionsValidator().Validate(
            name: null, new ExecutionLogCaptureOptions { MaxLines = maxLines, MaxBytes = maxBytes });

        result.Failed.Should().BeTrue("a bound that keeps nothing is a mistake, and not calling UseExecutionLogCapture() is the way to capture nothing");
        result.FailureMessage.Should().Contain(named);
    }

    [Test]
    public void TheDefaultBoundsAreAccepted()
    {
        new ExecutionLogCaptureOptionsValidator().Validate(name: null, new ExecutionLogCaptureOptions())
            .Succeeded.Should().BeTrue();
    }

    [Test]
    public void TheProviderHoldsNothingToDispose()
    {
        Action dispose = () => new ExecutionLogCaptureProvider().Dispose();
        dispose.Should().NotThrow();
    }
}
