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

using System;
using System.Collections.Specialized;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Test Trigger priority support.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class PriorityTest
    {
        // TODO rev 991 from terracotta not ported

        private static StringBuilder result;

        [SetUp]
        public void Setup()
        {
            result = new StringBuilder();
        }

        [Test]
        public async Task TestSameDefaultPriority()
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.threadPool.threadCount"] = "1";
            config["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool";
            config["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

            IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();

            DateTime n = DateTime.UtcNow;
            DateTime cal = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond, DateTimeKind.Utc);

            IMutableTrigger trig1 = new SimpleTriggerImpl("T1", null, cal);
            IMutableTrigger trig2 = new SimpleTriggerImpl("T2", null, cal);

            JobDetailImpl jobDetail = new JobDetailImpl("JD", null, typeof (TestJob));

            await sched.ScheduleJob(jobDetail, trig1);

            trig2.JobKey = new JobKey(jobDetail.Key.Name);
            await sched.ScheduleJob(trig2);

            await sched.Start();

            await Task.Delay(2000);

            Assert.AreEqual("T1T2", result.ToString());

            await sched.Shutdown();
        }

        [Test]
        public async Task TestDifferentPriority()
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.threadPool.threadCount"] = "1";
            config["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool";
            config["quartz.serializer.type"] = TestConstants.DefaultSerializerType;

            IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();

            DateTime n = DateTime.UtcNow.AddSeconds(1);
            DateTime cal = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond, DateTimeKind.Utc);

            IOperableTrigger trig1 = new SimpleTriggerImpl("T1", null, cal);
            trig1.Priority = 5;

            IOperableTrigger trig2 = new SimpleTriggerImpl("T2", null, cal);
            trig2.Priority = 10;

            JobDetailImpl jobDetail = new JobDetailImpl("JD", null, typeof (TestJob));

            await sched.ScheduleJob(jobDetail, trig1);

            trig2.JobKey = new JobKey(jobDetail.Key.Name);
            await sched.ScheduleJob(trig2);

            await sched.Start();

            await Task.Delay(2000);

            Assert.AreEqual("T2T1", result.ToString());

            await sched.Shutdown();
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        private class TestJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                result.Append(context.Trigger.Key.Name);
                return Task.CompletedTask;
            }
        }
    }
}