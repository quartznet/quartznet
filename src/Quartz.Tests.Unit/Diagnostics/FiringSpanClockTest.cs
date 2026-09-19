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

using FakeItEasy;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The clock a firing's span is timed by is read when there is a span to stamp with it, and not
/// otherwise (#3802).
/// </summary>
[TestFixture]
public class FiringSpanClockTest
{
    [Test]
    public void AFiringNobodyIsTracingReadsNoClock()
    {
        CountingTimeProvider clock = new();
        JobExecutionContextImpl context = Context();

        StartedActivity activity = QuartzActivitySource.StartJobExecute(context, clock);
        activity.Stop(clock, jobExEx: null);

        clock.Reads.Should().Be(0,
            "no listener means no span to stamp, so both readings would have been handed to a default struct and thrown away - and this is the shape of every scheduler that is not being traced");
    }

    [Test]
    public void AFiringSomebodyIsTracingIsStampedWithTheClockItIsGiven()
    {
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        SteppingTimeProvider clock = new(start, TimeSpan.FromSeconds(5));

        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        Activity recorded = null;
        listener.ActivityStopped = a => recorded = a;

        JobExecutionContextImpl context = Context();
        StartedActivity activity = QuartzActivitySource.StartJobExecute(context, clock);
        activity.Stop(clock, jobExEx: null);

        recorded.Should().NotBeNull("the listener records everything this source produces");
        recorded.StartTimeUtc.Should().Be(start.UtcDateTime,
            "the span is stamped with the clock the rest of the firing is timed by, not with Activity's own");
        recorded.Duration.Should().Be(TimeSpan.FromSeconds(5),
            "the second reading is the end of the span, so the duration is what that clock says elapsed");
        clock.Reads.Should().Be(2, "one reading to open the span and one to close it, and no more");
    }

    private static JobExecutionContextImpl Context()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        ((IOperableTrigger) bundle.Trigger).FireInstanceId = "fire-1";
        return new JobExecutionContextImpl(A.Fake<IScheduler>(), bundle, A.Fake<IJob>());
    }

    private class CountingTimeProvider : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Next();
        }

        protected virtual DateTimeOffset Next() => DateTimeOffset.UnixEpoch;
    }

    private sealed class SteppingTimeProvider(DateTimeOffset start, TimeSpan step) : CountingTimeProvider
    {
        private DateTimeOffset next = start;

        protected override DateTimeOffset Next()
        {
            DateTimeOffset value = next;
            next += step;
            return value;
        }
    }
}
