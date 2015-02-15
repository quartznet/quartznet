#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Tests for CronTrigger.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class CronTriggerTest
    {
        /// <summary>
        /// Tests the cron trigger time zone should change when changed.
        /// </summary>
        [Test]
        public void TestCronTriggerTimeZone_TimeZoneShouldChangeWhenChanged()
        {
            string tzStr = "FLE Standard Time";
            if (TimeZoneInfo.Local.Id == tzStr)
            {
                tzStr = "GMT Standard Time";
            }
            TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById(tzStr);
            CronTriggerImpl trigger = new CronTriggerImpl();
            trigger.Name = "Quartz-579";
            trigger.Group = SchedulerConstants.DefaultGroup;
            trigger.TimeZone = tz;
            trigger.CronExpressionString = "0 0 12 * * ?";
            Assert.AreEqual(tz, trigger.TimeZone, "TimeZone was changed");
        }

        [Test]
        public void BasicCronTriggerTest()
        {
            CronTriggerImpl trigger = new CronTriggerImpl();
            trigger.Name = "Quartz-Sample";
            trigger.Group = SchedulerConstants.DefaultGroup;
            trigger.CronExpressionString = "0 0 12 1 1 ? 2099";
            trigger.StartTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);
            trigger.EndTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);

            Assert.IsNull(trigger.ComputeFirstFireTimeUtc(null));
        }

        [Test]
        public void TestPrecision()
        {
            IOperableTrigger trigger = new CronTriggerImpl();
            trigger.StartTimeUtc = new DateTime(1982, 6, 28, 13, 5, 5, 233);
            Assert.IsFalse(trigger.HasMillisecondPrecision);
            Assert.AreEqual(0, trigger.StartTimeUtc.Millisecond);
        }


        [Test]
        public void TestClone()
        {
            CronTriggerImpl trigger = new CronTriggerImpl();
            trigger.Name =("test");
            trigger.Group = ("testGroup");
            trigger.CronExpressionString = ("0 0 12 * * ?");
            ICronTrigger trigger2 = (ICronTrigger)trigger.Clone();

            Assert.AreEqual(trigger, trigger2, "Cloning failed");

            // equals() doesn't test the cron expression
            Assert.AreEqual("0 0 12 * * ?", trigger2.CronExpressionString, "Cloning failed for the cron expression");
        }

        // http://jira.opensymphony.com/browse/QUARTZ-558
        [Test]
        public void TestQuartz558()
        {
            CronTriggerImpl trigger = new CronTriggerImpl();
            trigger.Name =("test");
            trigger.Group = ("testGroup");
            ICronTrigger trigger2 = (ICronTrigger)trigger.Clone();

            Assert.AreEqual(trigger, trigger2, "Cloning failed");
        }

        [Test]
        public void TestMisfireInstructionValidity()
        {
            CronTriggerImpl trigger = new CronTriggerImpl();

            try
            {
                trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
                trigger.MisfireInstruction = MisfireInstruction.SmartPolicy;
                trigger.MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing;
                trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
            }
            catch (Exception)
            {
                Assert.Fail("Unexpected exception while setting misfire instruction.");
            }

            try
            {
                trigger.MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing + 1;

                Assert.Fail("Expected exception while setting invalid misfire instruction but did not get it.");
            }
            catch (Exception ex)
            {
                if (ex is AssertionException)
                {
                    throw;
                }
            }
        }

        [Test]
        public void ShouldBeAbleToGetUnderlyingTriggerBuilder()
        {
            DateTimeOffset startTime = new DateTimeOffset(2012, 12, 30, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset endTime = new DateTimeOffset(2015, 1, 15, 0, 0, 0, TimeSpan.Zero);

            var trigger = TriggerBuilder.Create().WithCronSchedule("0 0 12 * * ?")
                .StartAt(startTime)
                .EndAt(endTime)
                .WithPriority(2)
                .Build();
            var triggerBuilder = trigger.GetTriggerBuilder();
            var trigger2 = triggerBuilder.Build();

            Assert.That(trigger.StartTimeUtc, Is.EqualTo(trigger2.StartTimeUtc));
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(trigger2.EndTimeUtc));
            Assert.That(trigger.Priority, Is.EqualTo(trigger2.Priority));
        }

        [Test]
        public void ShouldGetScheduleBuilderWithSameSettingsAsTrigger()
        {
            var startTime = DateTimeOffset.UtcNow;
            var endTime = DateTimeOffset.UtcNow.AddDays(1);
            var trigger = new CronTriggerImpl("name", "group", "jobname", "jobgroup", startTime, endTime, "0 0 12 * * ?", TimeZoneInfo.Utc);
            trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
            var scheduleBuilder = trigger.GetScheduleBuilder();

            var cloned = (CronTriggerImpl)scheduleBuilder.Build();
            Assert.That(cloned.MisfireInstruction, Is.EqualTo(trigger.MisfireInstruction));
            Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
            Assert.That(cloned.CronExpressionString, Is.EqualTo(trigger.CronExpressionString));
        }
    }
}