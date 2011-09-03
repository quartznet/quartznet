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
using System.Collections.Generic;

using Quartz;
using Quartz.Spi;
using Quartz.Job;
using Quartz.Impl;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{

    /// <summary>
    /// Unit test for DailyTimeIntervalScheduleBuilder.
    /// </summary>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    [TestFixture]
    public class DailyTimeIntervalScheduleBuilderTest
    {
    
	    [Test]
        public void TestScheduleActualTrigger()  
        {
		    IScheduler scheduler = StdSchedulerFactory.DefaultScheduler;
		    IJobDetail job = JobBuilder.Create(typeof (NoOpJob)).Build(); 
                
		    ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
						        .WithIntervalInSeconds(3))
						        .Build();
		    
            scheduler.ScheduleJob(job, trigger); //We are not verify anything other than just run through the scheduler.
		    scheduler.Shutdown();
	    }
	
	    
        [Test]
        public void TestHourlyTrigger() 
        {
		    IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                                                            .WithIdentity("test")
                                                            .WithSchedule(
                                                                DailyTimeIntervalScheduleBuilder.Create()
						                                        .WithIntervalInHours(3))
						                                    .Build();
		    Assert.AreEqual("test", trigger.Key.Name);
		    Assert.AreEqual("DEFAULT", trigger.Key.Group);
		    Assert.AreEqual(IntervalUnit.Hour, trigger.RepeatIntervalUnit);
		    //Assert.AreEqual(1, trigger.RepeatInterval);
		    IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
	    }
	
	    [Test]
        public void TestMinutelyTriggerWithTimeOfDay() 
        {
		    IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test", "group")
				.WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
						        .WithIntervalInMinutes(72)
						        .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
						        .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0))
						        .OnMondayThroughFriday())
						        .Build();
		    
            Assert.AreEqual("test", trigger.Key.Name);
		    Assert.AreEqual("group", trigger.Key.Group);
		    Assert.AreEqual(true, SystemTime.UtcNow() >= trigger.StartTimeUtc);
		    Assert.AreEqual(true, null == trigger.EndTimeUtc);
		    Assert.AreEqual(IntervalUnit.Minute, trigger.RepeatIntervalUnit);
		    Assert.AreEqual(72, trigger.RepeatInterval);
		    Assert.AreEqual(new TimeOfDay(8, 0), trigger.StartTimeOfDayUtc);
		    Assert.AreEqual(new TimeOfDay(17, 0), trigger.EndTimeOfDayUtc);
		    IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
	    }
	
	    [Test]
        public void TestSecondlyTriggerWithStartAndEndTime() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0,  0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 2, 1, 2011);
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger)TriggerBuilder.Create()
                .WithIdentity("test", "test")
                .WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
						    .WithIntervalInSeconds(121)
						    .StartingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(10, 0, 0))
                            .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
						    .OnSaturdayAndSunday())
						    .StartAt(startTime)
						    .EndAt(endTime)
						    .Build();
            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("test", trigger.Key.Group);
            Assert.AreEqual(true, startTime == trigger.StartTimeUtc);
            Assert.AreEqual(true, endTime == trigger.EndTimeUtc);
            Assert.AreEqual(IntervalUnit.Second, trigger.RepeatIntervalUnit);
            Assert.AreEqual(121, trigger.RepeatInterval);
            Assert.AreEqual(new TimeOfDay(10, 0, 0), trigger.StartTimeOfDayUtc);
            Assert.AreEqual(new TimeOfDay(23, 59, 59), trigger.EndTimeOfDayUtc);
		    IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
	    } 
    
    }
}
