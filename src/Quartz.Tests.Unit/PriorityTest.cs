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

using System.Collections.Specialized;
using System.Text;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// Test Trigger priority support.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class PriorityTest
{
    private static StringBuilder result;
    private static CountdownEvent countdownEvent;

    [SetUp]
    public void Setup()
    {
        result = new StringBuilder();
        countdownEvent = new CountdownEvent(2);
    }

    [TearDown]
    public void TearDown()
    {
        countdownEvent.Dispose();
    }

    [Test]
    public async Task TestSameDefaultPriority()
    {
        NameValueCollection config = new NameValueCollection();
        config["quartz.threadPool.threadCount"] = "1";
        config["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool";
        config["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();

        DateTime n = DateTime.UtcNow;
        DateTime date = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond, DateTimeKind.Utc);

        IMutableTrigger trig1 = new SimpleTriggerImpl { Key = new TriggerKey("T1"), StartTimeUtc = date };
        IMutableTrigger trig2 = new SimpleTriggerImpl { Key = new TriggerKey("T2"), StartTimeUtc = date };

        JobDetailImpl jobDetail = new JobDetailImpl("JD", typeof(TestJob));

        await scheduler.ScheduleJob(jobDetail, trig1);

        trig2.JobKey = new JobKey(jobDetail.Key.Name);
        await scheduler.ScheduleJob(trig2);

        await scheduler.Start();

        countdownEvent.Wait();

        Assert.That(result.ToString(), Is.EqualTo("T1T2"));

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TestDifferentPriority()
    {
        NameValueCollection config = new NameValueCollection();
        config["quartz.threadPool.threadCount"] = "1";
        config["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool";
        config["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();

        DateTime n = DateTime.UtcNow.AddSeconds(1);
        DateTime date = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond, DateTimeKind.Utc);

        IOperableTrigger trig1 = new SimpleTriggerImpl { Key = new TriggerKey("T1"), StartTimeUtc = date };
        trig1.Priority = 5;

        IOperableTrigger trig2 = new SimpleTriggerImpl { Key = new TriggerKey("T2"), StartTimeUtc = date };
        trig2.Priority = 10;

        JobDetailImpl jobDetail = new JobDetailImpl("JD", typeof(TestJob));

        await scheduler.ScheduleJob(jobDetail, trig1);

        trig2.JobKey = new JobKey(jobDetail.Key.Name);
        await scheduler.ScheduleJob(trig2);

        await scheduler.Start();

        countdownEvent.Wait();

        Assert.That(result.ToString(), Is.EqualTo("T2T1"));

        await scheduler.Shutdown();
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    private sealed class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            result.Append(context.Trigger.Key.Name);
            countdownEvent.Signal();
            return default;
        }
    }
}