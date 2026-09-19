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
    /// its branch: mid-execution the value is the wall clock less the fire time, and once the scheduler
    /// has measured the run it is what the scheduler stored.
    /// </summary>
    [Test]
    public void JobRunTimeEstimatesWhileRunningAndReportsTheMeasurementOnceStored()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        bundle = bundle with { FireTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };

        JobExecutionContextImpl ctx = new(null, bundle, null);

        ctx.JobRunTime.Should().BeGreaterThan(TimeSpan.Zero,
            "a job that fired five minutes ago and has not finished has run for about five minutes");
        ctx.JobRunTime.Should().NotBe(TimeSpan.MinValue,
            "TimeSpan.MinValue is what the documentation used to promise and what the property has never returned");

        ctx.JobRunTime = TimeSpan.FromMilliseconds(17);

        ctx.JobRunTime.Should().Be(TimeSpan.FromMilliseconds(17),
            "once the scheduler has measured the completed run, that measurement is the answer and the "
            + "wall-clock estimate is not consulted again");
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
