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

            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                                            {
                                                                StartTimeUtc = startCalendar,
                                                                RepeatIntervalUnit = IntervalUnit.Year,
                                                                RepeatInterval = 2  // every two years;
                                                            };

            DateTimeOffset targetCalendar = startCalendar.AddYears(4);

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
            DateTimeOffset thirdTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, thirdTime, "Year increment result not as expected.");
        }


        [Test]
        public void TestMonthlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                                            {
                                                                StartTimeUtc = startCalendar,
                                                                RepeatIntervalUnit = IntervalUnit.Month,
                                                                RepeatInterval = 5  // every five months
                                                            };

            DateTimeOffset targetCalendar = startCalendar.AddMonths(25); // jump 25 five months (5 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset sixthTime = fireTimes[5]; // get the sixth fire time

            Assert.AreEqual(targetCalendar, sixthTime, "Month increment result not as expected.");
        }

        [Test]
        public void TestWeeklyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                                            {
                                                                StartTimeUtc = startCalendar,
                                                                RepeatIntervalUnit = IntervalUnit.Week,
                                                                RepeatInterval = 6  // every six weeks
                                                            };


            DateTimeOffset targetCalendar = startCalendar.AddDays(7 * 6 * 4); // jump 24 weeks (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 7);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Week increment result not as expected.");
        }

        [Test]
        public void TestDailyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            var dailyTrigger = new CalendarIntervalTriggerImpl
                                                           {
                                                               StartTimeUtc = startCalendar,
                                                               RepeatIntervalUnit = IntervalUnit.Day,
                                                               RepeatInterval = 90  // every ninety days
                                                           };

            DateTimeOffset targetCalendar = startCalendar.AddDays(360); // jump 360 days (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Day increment result not as expected.");
        }

        [Test]
        public void TestHourlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);


            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                                            {
                                                                StartTimeUtc = startCalendar,
                                                                RepeatIntervalUnit = IntervalUnit.Hour,
                                                                RepeatInterval = 100    // every 100 hours
                                                            };

            DateTimeOffset targetCalendar = startCalendar.AddHours(400); // jump 400 hours (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Hour increment result not as expected.");
        }

        [Test]
        public void TestMinutelyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);


            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                    {
                                        StartTimeUtc = startCalendar,
                                        RepeatIntervalUnit = IntervalUnit.Minute,
                                        RepeatInterval = 100    // every 100 minutes
                                    };

            DateTimeOffset targetCalendar = startCalendar.AddMinutes(400); // jump 400 minutes (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Minutes increment result not as expected.");
        }

        [Test]
        public void TestSecondlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

            var yearlyTrigger = new CalendarIntervalTriggerImpl
                                                            {
                                                                StartTimeUtc = startCalendar,
                                                                RepeatIntervalUnit = IntervalUnit.Second,
                                                                RepeatInterval = 100    // every 100 seconds
                                                            };

            DateTimeOffset targetCalendar = startCalendar.AddSeconds(400); // jump 400 seconds (4 intervals)
            
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the third fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Seconds increment result not as expected.");
        }

        [Test]
        public void TestDaylightSavingsTransitions()
        {
            // Pick a day before a spring daylight savings transition...

            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 12, 3, 2010);

            var dailyTrigger = new CalendarIntervalTriggerImpl
                                                           {
                                                               StartTimeUtc = startCalendar,
                                                               RepeatIntervalUnit = IntervalUnit.Day,
                                                               RepeatInterval = 5   // every 5 days
                                                           };

            DateTimeOffset targetCalendar = startCalendar.AddDays(10); // jump 10 days (2 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset testTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over spring 2010 daylight savings transition.");

            // And again, Pick a day before a spring daylight savings transition... (QTZ-240)

            startCalendar = new DateTime(2011, 3, 12, 1, 0, 0);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 1; // every day

            targetCalendar = startCalendar.AddDays(2); // jump 2 days (2 intervals)

            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            testTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over spring 2011 daylight savings transition.");

            // And again, Pick a day before a spring daylight savings transition... (QTZ-240) - and prove time of day is not preserved without setPreserveHourOfDayAcrossDaylightSavings(true)

            var cetTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            startCalendar = TimeZoneInfo.ConvertTime(new DateTime(2011, 3, 26, 4, 0, 0), cetTimeZone);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval= 1; // every day

            targetCalendar = TimeZoneInfo.ConvertTime(startCalendar, cetTimeZone);
            targetCalendar = targetCalendar.AddDays(2); // jump 2 days (2 intervals)

            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);

            testTime = fireTimes[2]; // get the third fire time

            DateTimeOffset testCal = TimeZoneInfo.ConvertTime(testTime, cetTimeZone);

            Assert.AreNotEqual(targetCalendar.Hour, testCal.Hour, "Day increment time-of-day result not as expected over spring 2011 daylight savings transition.");

            // And again, Pick a day before a spring daylight savings transition... (QTZ-240) - and prove time of day is preserved with setPreserveHourOfDayAcrossDaylightSavings(true)

            startCalendar = TimeZoneInfo.ConvertTime(new DateTime(2011, 3, 26, 4, 0, 0), cetTimeZone);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 1; // every day
            dailyTrigger.TimeZone = cetTimeZone;
            dailyTrigger.PreserveHourOfDayAcrossDaylightSavings = true;

            targetCalendar = startCalendar.AddDays(2); // jump 2 days (2 intervals)

            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);

            testTime = fireTimes[2]; // get the third fire time

            testCal = TimeZoneInfo.ConvertTime(testTime, cetTimeZone);

            Assert.AreEqual(targetCalendar.Hour, testCal.Hour, "Day increment time-of-day result not as expected over spring 2011 daylight savings transition.");

            // Pick a day before a fall daylight savings transition...

            startCalendar = new DateTimeOffset(2010, 10, 31, 9, 30, 17, TimeSpan.Zero);

            dailyTrigger = new CalendarIntervalTriggerImpl
                               {
                                   StartTimeUtc = startCalendar,
                                   RepeatIntervalUnit = IntervalUnit.Day,
                                   RepeatInterval = 5   // every 5 days
                               };

            targetCalendar = startCalendar.AddDays(15); // jump 15 days (3 intervals)
            
            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            testTime = fireTimes[3]; // get the fourth fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over fall daylight savings transition.");
        }


        [Test]
        public void TestFinalFireTimes()
        {
            DateTimeOffset startCalendar = DateBuilder.DateOf(9, 0, 0, 12, 3, 2010);
            DateTimeOffset endCalendar = startCalendar.AddDays(10); // jump 10 days (2 intervals)

            var dailyTrigger = new CalendarIntervalTriggerImpl
                                                           {
                                                               StartTimeUtc = startCalendar,
                                                               RepeatIntervalUnit = IntervalUnit.Day,
                                                               RepeatInterval = 5,   // every 5 days
                                                               EndTimeUtc = endCalendar
                                                           };

            DateTimeOffset? testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.AreEqual(endCalendar, testTime, "Final fire time not computed correctly for day interval.");

            endCalendar = startCalendar.AddDays(15).AddMinutes(-2); // jump 15 days and back up 2 minutes
            dailyTrigger = new CalendarIntervalTriggerImpl
                               {
                                   StartTimeUtc = startCalendar,
                                   RepeatIntervalUnit = IntervalUnit.Minute,
                                   RepeatInterval = 5,   // every 5 minutes
                                   EndTimeUtc = endCalendar
                               };

            testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.IsTrue(endCalendar > (testTime), "Final fire time not computed correctly for minutely interval.");

            endCalendar = endCalendar.AddMinutes(-3); // back up three more minutes

            Assert.AreEqual(endCalendar, testTime, "Final fire time not computed correctly for minutely interval.");
        }

        [Test]
        public void TestMisfireInstructionValidity()
        {
            var trigger = new CalendarIntervalTriggerImpl();

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
            var jobDataMap = new JobDataMap();
            jobDataMap["A"] =  "B";

            var t = new CalendarIntervalTriggerImpl
                                                {
                                                    Name = "test",
                                                    Group = "testGroup",
                                                    CalendarName = "MyCalendar",
                                                    Description = "CronTriggerDesc",
                                                    JobDataMap = jobDataMap,
                                                    RepeatInterval = 5,
                                                    RepeatIntervalUnit = IntervalUnit.Day
                                                };

            return t;
        }


        protected override string[] GetVersions()
        {
            return versions;
        }

        protected override void VerifyMatch(object target, object deserialized)
        {
            var targetCalTrigger = (CalendarIntervalTriggerImpl) target;
            var deserializedCalTrigger = (CalendarIntervalTriggerImpl) deserialized;

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