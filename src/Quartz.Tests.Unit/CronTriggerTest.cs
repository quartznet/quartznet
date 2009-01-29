/* 
 * Copyright 2004-2006 OpenSymphony 
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
 */

using System;

using MbUnit.Framework;

#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Tests for CronTrigger.
    /// </summary>
    [TestFixture]
    public class CronTriggerTest
    {

#if NET_35
        /// <summary>
        /// Tests the cron trigger time zone should change when changed.
        /// </summary>
        [Test]
        public void TestCronTriggerTimeZone_TimeZoneShouldChangeWhenChanged()
        {
            string tzStr = "FLE Standard Time";
            if (TimeZone.Local.Id == tzStr)
            {
                tzStr = "GMT Standard Time";
            }
            TimeZone tz = TimeZone.FindSystemTimeZoneById(tzStr);
            CronTrigger trigger = new CronTrigger();
            trigger.Name = "Quartz-579";
            trigger.Group = SchedulerConstants.DefaultGroup;
            trigger.TimeZone = tz;
            trigger.CronExpressionString = "0 0 12 * * ?";
            Assert.AreEqual(tz, trigger.TimeZone, "TimeZone was changed");
        }
#endif

        [Test]
        public void BasicCronTriggerTest()
        {
            CronTrigger trigger = new CronTrigger();
            trigger.Name = "Quartz-Sample";
            trigger.Group = SchedulerConstants.DefaultGroup;
            trigger.CronExpressionString = "0 0 12 1 1 ? 2099";
            trigger.StartTimeUtc = new DateTime(2099, 1, 1, 12, 0, 1);
            trigger.EndTimeUtc = new DateTime(2099, 1, 1, 12, 0, 1);

            Assert.IsNull(trigger.ComputeFirstFireTimeUtc(null));
        }
    }
}