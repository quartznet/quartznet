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

        IOperableTrigger trigger = new SimpleTriggerImpl("triggerName", "triggerGroup");
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
}