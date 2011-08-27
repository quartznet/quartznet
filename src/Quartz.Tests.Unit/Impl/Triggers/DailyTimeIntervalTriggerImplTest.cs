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
using Generic = System.Collections.Generic;

using NUnit.Framework;

using Quartz;
using Quartz.Collection;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;



namespace Quartz.Tests.Unit.Impl.Triggers
{
    /// <summary>
    /// Unit test for <see cref="DailyTimeIntervalTriggerImpl"/>.
    /// </summary>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    [TestFixture]
    public class DailyTimeIntervalTriggerImplTest
    {
    
	    [Test]
        public void TestNormalExample() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 72;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);

            int i = 0;
            foreach(DateTimeOffset t in fireTimes)
            {
                Console.WriteLine("#"+(i++)+ " " + t);
            }
            
            
            Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(10, 24, 0, 16, 1, 2011), fireTimes[47]);
	    }
    
	    [Test]
        public void TestQuartzCalendarExclusion() 
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = new TimeOfDay(8, 0);
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    CronCalendar cronCal = new CronCalendar("* * 9-12 * * ?"); // exclude 9-12		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, cronCal, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(13, 0, 0, 1, 1, 2011), fireTimes[1]);
            Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 4, 1, 2011), fireTimes[47]);
	    }
    
	    [Test]
        public void TestValidateTimeOfDayOrder() 
        {
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeOfDayUtc = new TimeOfDay(12, 0, 0);
		    trigger.EndTimeOfDayUtc = new TimeOfDay(8, 0, 0);
		    try 
            {
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when time of day is not in order.");
		    } catch (SchedulerException) 
            {
			    // expected.
		    }
	    }

	    [Test]
        public void TestValidateInterval()  
        {
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.Name = "test";
		    trigger.Group = "test";
            trigger.JobKey = JobKey.CreateJobKey("test");
		
		    trigger.RepeatIntervalUnit = IntervalUnit.Hour;
		    trigger.RepeatInterval = 25;
		    try 
            {
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when interval is greater than 24 hours.");
		    } catch (SchedulerException) 
            {
			    // expected.
		    }
		
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60 * 25;
		    try 
            {
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when interval is greater than 24 hours.");
		    } catch (SchedulerException) 
            {
			    // expected.
		    }

		    trigger.RepeatIntervalUnit = IntervalUnit.Second;
		    trigger.RepeatInterval = 60 * 60 * 25;
		    try 
            {
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when interval is greater than 24 hours.");
		    } catch (SchedulerException) 
            {
			    // expected.
		    }
		
		    try 
            {
			    trigger.RepeatIntervalUnit = IntervalUnit.Day;
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when interval unit > HOUR.");
		    } catch (Exception) 
            {
			    // expected.
		    }

		    try 
            {
			    trigger.RepeatIntervalUnit = IntervalUnit.Second;
			    trigger.RepeatInterval = 0;
			    trigger.Validate();
			    Assert.Fail("Trigger should be invalidate when interval is zero.");
		    } catch (Exception) 
            {
			    // expected.
		    }
	    }

	
        [Test]
        public void TestStartTimeWithoutStartTimeOfDay()  
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 2, 1, 2011), fireTimes[47]);
	    }

	    [Test]
        public void TestEndTimeWithoutEndTimeOfDay() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(22, 0, 0, 2, 1, 2011);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.EndTimeUtc = endTime;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(47, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(22, 0, 0, 2, 1, 2011), fireTimes[46]);
	    }
	
	    [Test]
        public void TestStartTimeBeforeStartTimeOfDay() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 3, 1, 2011), fireTimes[47]);
	    }
	
	    [Test]
        public void TestStartTimeAfterStartTimeOfDay()
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(9, 23, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(10, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(9, 0, 0, 4, 1, 2011), fireTimes[47]);
	    }
	
	    [Test]
        public void TestEndTimeBeforeEndTimeOfDay()
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(16, 0, 0, 2, 1, 2011);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.EndTimeUtc = endTime;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(35, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[17]);
		    Assert.AreEqual(DateBuilder.DateOf(16, 0, 0, 2, 1, 2011), fireTimes[34]);
	    }
	
	    [Test]
        public void TestEndTimeAfterEndTimeOfDay() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(18, 0, 0, 2, 1, 2011);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.EndTimeUtc = endTime;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(36, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[17]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 2, 1, 2011), fireTimes[35]);
	    }
	
	    [Test]
        public void TestTimeOfDayWithStartTime()  
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
		    Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011), fireTimes[47]);
	    }
	
	    [Test]
        public void TestTimeOfDayWithEndTime() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 4, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.EndTimeUtc = endTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(30, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 3, 1, 2011), fireTimes[29]);
	    }

	    [Test]
        public void TestTimeOfDayWithEndTime2() 
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 23, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(23, 59, 59); // edge case when endTime is last second of day, which is default too.
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);		
		    Assert.AreEqual(DateBuilder.DateOf(8, 23, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(23, 23, 0, 3, 1, 2011), fireTimes[47]);
	    }
	
	    [Test]
        public void TestAllDaysOfTheWeek()
        {
		    ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek;
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.DaysOfWeek = daysOfWeek;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
		    Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011), fireTimes[47]);
	    }

	    [Test]
        public void TestMonThroughFri() 
        {
		    ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.MondayThroughFriday;
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.DaysOfWeek = daysOfWeek;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DayOfWeek.Monday, fireTimes[0].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 4, 1, 2011), fireTimes[10]);
		    Assert.AreEqual(DayOfWeek.Tuesday, fireTimes[10].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 7, 1, 2011), fireTimes[47]);
		    Assert.AreEqual(DayOfWeek.Friday, fireTimes[47].DayOfWeek);
	    }

        [Test]
        public void TestSatAndSun() 
        {
		    ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.SaturdayAndSunday;
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.DaysOfWeek = daysOfWeek;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DayOfWeek.Saturday, fireTimes[0].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 2, 1, 2011), fireTimes[10]);
		    Assert.AreEqual(DayOfWeek.Sunday, fireTimes[10].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 15, 1, 2011), fireTimes[47]);
		    Assert.AreEqual(DayOfWeek.Saturday, fireTimes[47].DayOfWeek);
	    }

	    [Test]
        public void TestMonOnly() 
        {
		    ISet<DayOfWeek> daysOfWeek = new HashSet<DayOfWeek>();
		    daysOfWeek.Add(DayOfWeek.Monday);
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.DaysOfWeek = daysOfWeek;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 60;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DayOfWeek.Monday, fireTimes[0].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 10, 1, 2011), fireTimes[10]);
		    Assert.AreEqual(DayOfWeek.Monday, fireTimes[10].DayOfWeek);
		    Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 31, 1, 2011), fireTimes[47]);
		    Assert.AreEqual(DayOfWeek.Monday, fireTimes[47].DayOfWeek);
	    }
	
	
	    [Test]
        public void TestTimeOfDayWithEndTimeOddInterval()  
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 4, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
		    TimeOfDay endTimeOfDay = new TimeOfDay(10, 0, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.EndTimeUtc = endTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Minute;
		    trigger.RepeatInterval = 23;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(18, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(9, 55, 0, 1, 1, 2011), fireTimes[5]);
		    Assert.AreEqual(DateBuilder.DateOf(9, 55, 0, 3, 1, 2011), fireTimes[17]);
	    }
	
	    [Test]
        public void TestHourInterval()
        {
		    DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    DateTimeOffset endTime = DateBuilder.DateOf(13, 0, 0, 15, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 1, 15);
		    TimeOfDay endTimeOfDay = new TimeOfDay(16, 1, 15);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeUtc = endTime;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Hour;
		    trigger.RepeatInterval = 2;
		
		    Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
		    Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual(DateBuilder.DateOf(8, 1, 15, 1, 1, 2011), fireTimes[0]);
		    Assert.AreEqual(DateBuilder.DateOf(12, 1, 15, 10, 1, 2011), fireTimes[47]);
	    }
	
	    [Test]
        public void TestSecondInterval()  
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
		    TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 2);
		    TimeOfDay endTimeOfDay = new TimeOfDay(13, 30, 0);
		    DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
		    trigger.StartTimeUtc = startTime;
		    trigger.StartTimeOfDayUtc = startTimeOfDay;
		    trigger.EndTimeOfDayUtc = endTimeOfDay;
		    trigger.RepeatIntervalUnit = IntervalUnit.Second;
		    trigger.RepeatInterval = 72;

            Generic::IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
		    Assert.AreEqual((DateBuilder.DateOf(8, 0, 2, 1, 1, 2011)), fireTimes[0]);
		    Assert.AreEqual((DateBuilder.DateOf(8, 56, 26, 1, 1, 2011)), fireTimes[47]);
	    }
    }
}
