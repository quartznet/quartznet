using System;
using System.Collections.Generic;
using NUnit.Framework;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CalendarIntervalTriggerTest : SerializationTestSupport
    {
        private static readonly string[] versions = new[] {"2.0"};
        
        [Test]
        public void TestYearlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Year;
            yearlyTrigger.RepeatInterval = 2; // every two years;

            DateTimeOffset targetCalendar = startCalendar.AddYears(4);

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
            DateTimeOffset thirdTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, thirdTime, "Year increment result not as expected.");
        }


        [Test]
        public void TestMonthlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Month;
            yearlyTrigger.RepeatInterval = 5; // every five months

            DateTimeOffset targetCalendar = startCalendar.AddMonths(25); // jump 25 five months (5 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset sixthTime = fireTimes[5]; // get the sixth fire time

            Assert.AreEqual(targetCalendar, sixthTime, "Month increment result not as expected.");
        }

        [Test]
        public void TestWeeklyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);
            
            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Week;
            yearlyTrigger.RepeatInterval = 6; // every six weeks

            DateTimeOffset targetCalendar = startCalendar.AddDays(7 * 6 * 4); // jump 24 weeks (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 7);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Week increment result not as expected.");
        }

        [Test]
        public void TestDailyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);
            
            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 90; // every ninety days

            DateTimeOffset targetCalendar = startCalendar.AddDays(360); // jump 360 days (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Day increment result not as expected.");
        }

        [Test]
        public void TestHourlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Hour;
            yearlyTrigger.RepeatInterval = 100; // every 100 hours

            DateTimeOffset targetCalendar = startCalendar.AddHours(400); // jump 400 hours (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Hour increment result not as expected.");
        }

        [Test]
        public void TestMinutelyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);
            
            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Minute;
            yearlyTrigger.RepeatInterval = 100; // every 100 minutes

            DateTimeOffset targetCalendar = startCalendar.AddMinutes(400); // jump 400 minutes (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Minutes increment result not as expected.");
        }

        [Test]
        public void TestSecondlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);


            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = IntervalUnit.Second;
            yearlyTrigger.RepeatInterval = 100; // every 100 seconds

            DateTimeOffset targetCalendar = startCalendar.AddSeconds(400); // jump 400 seconds (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the third fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Seconds increment result not as expected.");
        }

        [Test]
        public void TestDaylightSavingsTransitions()
        {
            // Pick a day before a daylight savings transition...

            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 12, 3, 2010);
                
            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            DateTimeOffset targetCalendar = startCalendar.AddDays(10); // jump 10 days (2 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset testTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over spring daylight savings transition.");

            // Pick a day before a daylight savings transition...
            startCalendar = DateBuilder.DateOf(9, 30, 17, 31, 10, 2010);
            
            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            targetCalendar = startCalendar.AddDays(15); // jump 15 days (3 intervals)
            
            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            testTime = fireTimes[3]; // get the fourth fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over fall daylight savings transition.");
        }


        [Test]
        public void TestFinalFireTimes()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 0, 0, 12, 3, 2010);
            
            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            DateTimeOffset endCalendar = startCalendar.AddDays(10); // jump 10 days (2 intervals)
            dailyTrigger.EndTimeUtc = endCalendar;

            DateTimeOffset? testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.AreEqual(endCalendar, testTime, "Final fire time not computed correctly for day interval.");

            startCalendar = new DateTimeOffset(2010, 3, 12, 9, 0, 0, TimeSpan.Zero);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Minute;
            dailyTrigger.RepeatInterval = 5; // every 5 minutes

            endCalendar = startCalendar.AddDays(15).AddMinutes(-2); // back up two minutes
            dailyTrigger.EndTimeUtc = endCalendar;

            testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.IsTrue((endCalendar > (testTime)), "Final fire time not computed correctly for minutely interval.");

            endCalendar = endCalendar.AddMinutes(-3); // back up three more minutes

            Assert.IsTrue((endCalendar.Equals(testTime)), "Final fire time not computed correctly for minutely interval.");
        }

        [Test]
        public void TestMisfireInstructionValidity()
        {
            CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl();

            try
            {
                trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
                trigger.MisfireInstruction = MisfireInstruction.SmartPolicy;
                trigger.MisfireInstruction = MisfireInstruction.CalendarIntervalTrigger.DoNothing;
                trigger.MisfireInstruction = MisfireInstruction.CalendarIntervalTrigger.FireOnceNow;
            }
            catch (Exception)
            {
                Assert.Fail("Unexpected exception while setting misfire instruction.");
            }

            try
            {
                trigger.MisfireInstruction = MisfireInstruction.CalendarIntervalTrigger.DoNothing + 1;

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

        protected override object GetTargetObject()
        {
            JobDataMap jobDataMap = new JobDataMap();
            jobDataMap["A"] =  "B";

            CalendarIntervalTriggerImpl t = new CalendarIntervalTriggerImpl();
            t.Name = "test";
            t.Group = "testGroup";
            t.CalendarName = "MyCalendar";
            t.Description = "CronTriggerDesc";
            t.JobDataMap = jobDataMap;
            t.RepeatInterval = 5;
            t.RepeatIntervalUnit = IntervalUnit.Day;

            return t;
        }


        protected override string[] GetVersions()
        {
            return versions;
        }

        protected override void VerifyMatch(object target, object deserialized)
        {
            CalendarIntervalTriggerImpl targetCalTrigger = (CalendarIntervalTriggerImpl) target;
            CalendarIntervalTriggerImpl deserializedCalTrigger = (CalendarIntervalTriggerImpl) deserialized;

            Assert.IsNotNull(deserializedCalTrigger);
            Assert.AreEqual(targetCalTrigger.Name, deserializedCalTrigger.Name);
            Assert.AreEqual(targetCalTrigger.Group, deserializedCalTrigger.Group);
            Assert.AreEqual(targetCalTrigger.JobName, deserializedCalTrigger.JobName);
            Assert.AreEqual(targetCalTrigger.JobGroup, deserializedCalTrigger.JobGroup);
//        assertEquals(targetCronTrigger.getStartTime), deserializedCronTrigger.getStartTime());
            Assert.AreEqual(targetCalTrigger.EndTimeUtc, deserializedCalTrigger.EndTimeUtc);
            Assert.AreEqual(targetCalTrigger.CalendarName, deserializedCalTrigger.CalendarName);
            Assert.AreEqual(targetCalTrigger.Description, deserializedCalTrigger.Description);
            Assert.AreEqual(targetCalTrigger.JobDataMap, deserializedCalTrigger.JobDataMap);
            Assert.AreEqual(targetCalTrigger.RepeatInterval, deserializedCalTrigger.RepeatInterval);
            Assert.AreEqual(targetCalTrigger.RepeatIntervalUnit, deserializedCalTrigger.RepeatIntervalUnit);
        }
    }
}