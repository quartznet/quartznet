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

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Jobs;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for JobExecutionContext.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public class JobExecutionContextTest
{
    [Test]
    public void TestToString()
    {
        // QRTZNET-48
        IJobExecutionContext ctx = new JobExecutionContextImpl(null, TestUtil.NewMinimalTriggerFiredBundle(), null);
        ctx.ToString();
    }

    [Test]
    public void RecoveryTriggerKeyAndGroup()
    {
        IJobExecutionContext ctx = new JobExecutionContextImpl(null, TestUtil.NewMinimalRecoveringTriggerFiredBundle(), null);
        ctx.MergedJobDataMap[SchedulerConstants.FailedJobOriginalTriggerName] = "originalTriggerName";
        ctx.MergedJobDataMap[SchedulerConstants.FailedJobOriginalTriggerGroup] = "originalTriggerGroup";
        var recoveringTriggerKey = ctx.RecoveringTriggerKey;
        Assert.Multiple(() =>
        {
            Assert.That(recoveringTriggerKey, Is.Not.Null);
            Assert.That(recoveringTriggerKey.Name, Is.EqualTo("originalTriggerName"));
            Assert.That(recoveringTriggerKey.Group, Is.EqualTo("originalTriggerGroup"));
        });
    }

    [Test]
    public void MergedJobDataMapIsFullyPopulatedAndBuiltOnce()
    {
        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity(new JobKey("jobName", "jobGroup"))
            .UsingJobData("jobOnly", "jobValue")
            .UsingJobData("shared", "fromJob")
            .Build();

        IOperableTrigger trigger = new SimpleTriggerImpl { Key = new TriggerKey("triggerName", "triggerGroup"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        trigger.JobDataMap["triggerOnly"] = "triggerValue";
        trigger.JobDataMap["shared"] = "fromTrigger";

        TriggerFiredBundle bundle = new TriggerFiredBundle
        {
            JobDetail = jobDetail,
            Trigger = trigger,
            Recovering = false,
            FireTimeUtc = DateTimeOffset.UtcNow,
            ScheduledFireTimeUtc = null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null,
        };

        IJobExecutionContext ctx = new JobExecutionContextImpl(null, bundle, null);

        JobDataMap merged = ctx.MergedJobDataMap;

        merged.Count.Should().Be(3, "the merged map holds the union of the job's and the trigger's keys");
        merged.GetString("jobOnly").Should().Be("jobValue");
        merged.GetString("triggerOnly").Should().Be("triggerValue");
        merged.GetString("shared").Should().Be("fromTrigger", "the trigger's value overrides the job's");

        ctx.MergedJobDataMap.Should().BeSameAs(merged,
            "the map is merged once and then published, so every later read sees that same fully built instance");
    }

    /// <summary>
    /// The documentation used to promise <see cref="TimeSpan.MinValue" /> until the job completed, and
    /// a listener written against it (<c>if (context.JobRunTime == TimeSpan.MinValue)</c>) never took
    /// its branch. Mid-execution the value was then the wall clock less the fire time, which a fake clock
    /// could not move (#4097); it is now the time since the run began, on the context's clock, and once
    /// the run has stopped it is the measurement.
    /// </summary>
    [Test]
    public void JobRunTimeCountsFromTheRunsStartOnItsClockAndHoldsTheMeasurementOnceStopped()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 9, 0, 0, TimeSpan.Zero));
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle() with { FireTimeUtc = clock.GetUtcNow().AddMinutes(-5) };

        JobExecutionContextImpl ctx = new(null, bundle, null, inputSerializer: null, clock);

        ctx.JobRunTime.Should().Be(TimeSpan.Zero, "a firing whose job has not been called yet has run for no time at all");

        ctx.StartRunClock();
        clock.Advance(TimeSpan.FromSeconds(7));

        ctx.JobRunTime.Should().Be(TimeSpan.FromSeconds(7),
            "the run counts from its start on the context's clock, not from a fire time five minutes earlier");
        ctx.JobRunTime.Should().NotBe(TimeSpan.MinValue,
            "TimeSpan.MinValue is what the documentation used to promise and what the property has never returned");

        ctx.StopRunClock();
        clock.Advance(TimeSpan.FromMinutes(1));

        ctx.JobRunTime.Should().Be(TimeSpan.FromSeconds(7),
            "once the run has stopped, its measurement is the answer and the clock is not consulted again");
    }

    /// <summary>
    /// A context built with the public constructor is built to be handed to a job straight away, so its run
    /// counts from its construction, on the system clock — never from <see cref="DateTimeOffset.UtcNow" />
    /// less the fire time.
    /// </summary>
    [Test]
    public void AContextBuiltByHandCountsFromItsConstructionOnTheSystemClock()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle() with { FireTimeUtc = DateTimeOffset.UtcNow.AddHours(-1) };
        long before = TimeProvider.System.GetTimestamp();

        JobExecutionContextImpl ctx = new(null, bundle, null);

        TimeSpan runTime = ctx.JobRunTime;
        TimeSpan sinceBefore = TimeProvider.System.GetElapsedTime(before);
        runTime.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(sinceBefore,
                "the run began when the context was built, after the timestamp taken before it, and not when the trigger fired an hour ago");
    }

    /// <summary>
    /// The two values a context builds on demand are published with an interlocked compare-and-exchange
    /// rather than under a lock of its own (#3802), so what has to hold is that racing readers all get
    /// the same one: a value a middleware puts into the merged map has to reach the job, and a token
    /// handed to one caller has to be the token Interrupt cancels.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void RacingReadersOfTheLazyValuesAllSeeTheSameOne(bool readTheMap)
    {
        const int Racers = 32;
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        JobExecutionContextImpl ctx = new(null, bundle, null);

        object[] seen = new object[Racers];
        using Barrier start = new(Racers);

        Parallel.For(0, Racers, i =>
        {
            start.SignalAndWait();
            seen[i] = readTheMap ? ctx.MergedJobDataMap : ctx.CancellationToken.WaitHandle;
        });

        seen.Should().AllSatisfy(value => value.Should().BeSameAs(seen[0]),
            "the first writer wins and every loser drops what it built, so the context has exactly one of each");
    }

    /// <summary>
    /// A guard rather than a behaviour: the lock that used to guard those two fields was an object per
    /// firing, and the compare-and-exchange above is what replaced it.
    /// </summary>
    [Test]
    public void TheContextCarriesNoLockOfItsOwn()
    {
        System.Reflection.FieldInfo[] fields = typeof(JobExecutionContextImpl)
            .GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        fields.Should().NotContain(field => field.FieldType == typeof(Lock),
            "a firing allocates one of these, and the two fields the lock guarded are written at most once each - which is what an interlocked compare-and-exchange is for");
    }
}
