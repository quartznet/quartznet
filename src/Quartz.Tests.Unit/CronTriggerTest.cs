#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
            TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tzStr);
            CronTrigger trigger = new CronTrigger();
            trigger.Name = "Quartz-579";
            trigger.Group = SchedulerConstants.DefaultGroup;
            trigger.TimeZone = tz;
            trigger.CronExpressionString = "0 0 12 * * ?";
            Assert.AreEqual(tz, trigger.TimeZone, "TimeZone was changed");
        }

        [Test]
        public void BasicCronTriggerTest()
        {
            CronTrigger trigger = new CronTrigger();
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
            Trigger trigger = new CronTrigger();
            trigger.StartTimeUtc = new DateTime(1982, 6, 28, 13, 5, 5, 233);
            Assert.IsFalse(trigger.HasMillisecondPrecision);
            Assert.AreEqual(0, trigger.StartTimeUtc.Millisecond);
        }
    }
}