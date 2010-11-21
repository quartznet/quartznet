using System;
using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CalendarIntervalTriggerTest : SerializationTestSupport
    {
        private static string[] VERSIONS = new string[] {"2.0"};


        [Test]
        public void testYearlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateBuilder.IntervalUnit.YEAR;
            yearlyTrigger.RepeatInterval = 2; // every two years;

            DateTimeOffset targetCalendar = new DateTimeOffset(2009, 6, 1, 9, 30, 17, TimeSpan.Zero); // jump 4 years (2 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
            DateTimeOffset secondTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, secondTime, "Year increment result not as expected.");
        }


        [Test]
        public void testMonthlyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.setStartTime(startCalendar.getTime());
            yearlyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.MONTH);
            yearlyTrigger.setRepeatInterval(5); // every five months

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.MONTH, 25); // jump 25 five months (5 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(yearlyTrigger, null, 6);
            Date fifthTime = (Date) fireTimes.get(5); // get the sixth fire time

            Assert.AreEqual("Month increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testWeeklyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.setStartTime(startCalendar.getTime());
            yearlyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.WEEK);
            yearlyTrigger.setRepeatInterval(6); // every six weeks

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.DAY_OF_YEAR, 7*6*4); // jump 24 weeks (4 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(yearlyTrigger, null, 7);
            Date fifthTime = (Date) fireTimes.get(4); // get the fifth fire time

            System.out.
            println("targetCalendar:" + targetCalendar.getTime());
            System.out.
            println("fifthTimee" + fifthTime);

            Assert.AreEqual("Week increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testDailyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.setStartTime(startCalendar.getTime());
            dailyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.DAY);
            dailyTrigger.setRepeatInterval(90); // every ninety days

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.DAY_OF_YEAR, 360); // jump 360 days (4 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(dailyTrigger, null, 6);
            Date fifthTime = (Date) fireTimes.get(4); // get the fifth fire time

            Assert.AreEqual("Day increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testHourlyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.setStartTime(startCalendar.getTime());
            yearlyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.HOUR);
            yearlyTrigger.setRepeatInterval(100); // every 100 hours

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.HOUR, 400); // jump 400 hours (4 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(yearlyTrigger, null, 6);
            Date fifthTime = (Date) fireTimes.get(4); // get the fifth fire time

            Assert.AreEqual("Hour increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testMinutelyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.setStartTime(startCalendar.getTime());
            yearlyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.MINUTE);
            yearlyTrigger.setRepeatInterval(100); // every 100 minutes

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.MINUTE, 400); // jump 400 minutes (4 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(yearlyTrigger, null, 6);
            Date fifthTime = (Date) fireTimes.get(4); // get the fifth fire time

            Assert.AreEqual("Minutes increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testSecondlyIntervalGetFireTimeAfter()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl yearlyTrigger = new CalendarIntervalTriggerImpl();
            yearlyTrigger.setStartTime(startCalendar.getTime());
            yearlyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.SECOND);
            yearlyTrigger.setRepeatInterval(100); // every 100 seconds

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.set(2005, Calendar.JUNE, 1, 9, 30, 17);
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.SECOND, 400); // jump 400 seconds (4 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(yearlyTrigger, null, 6);
            Date fifthTime = (Date) fireTimes.get(4); // get the third fire time

            Assert.AreEqual("Seconds increment result not as expected.", targetCalendar.getTime(), fifthTime);
        }

        [Test]
        public void testDaylightSavingsTransitions()
        {
            // Pick a day before a daylight savings transition...

            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2010, Calendar.MARCH, 12, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.setStartTime(startCalendar.getTime());
            dailyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.DAY);
            dailyTrigger.setRepeatInterval(5); // every 5 days

            Calendar targetCalendar = Calendar.getInstance();
            targetCalendar.setTime(startCalendar.getTime());
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.DAY_OF_YEAR, 10); // jump 10 days (2 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            List fireTimes = TriggerUtils.computeFireTimes(dailyTrigger, null, 6);
            Date testTime = (Date) fireTimes.get(2); // get the third fire time

            Assert.AreEqual("Day increment result not as expected over spring daylight savings transition.", targetCalendar.getTime(), testTime);


            // Pick a day before a daylight savings transition...

            startCalendar = Calendar.getInstance();
            startCalendar.set(2010, Calendar.OCTOBER, 31, 9, 30, 17);
            startCalendar.clear(Calendar.MILLISECOND);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.setStartTime(startCalendar.getTime());
            dailyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.DAY);
            dailyTrigger.setRepeatInterval(5); // every 5 days

            targetCalendar = Calendar.getInstance();
            targetCalendar.setTime(startCalendar.getTime());
            targetCalendar.setLenient(true);
            targetCalendar.add(Calendar.DAY_OF_YEAR, 15); // jump 15 days (3 intervals)
            targetCalendar.clear(Calendar.MILLISECOND);

            fireTimes = TriggerUtils.computeFireTimes(dailyTrigger, null, 6);
            testTime = (Date) fireTimes.get(3); // get the fourth fire time

            Assert.AreEqual("Day increment result not as expected over fall daylight savings transition.", targetCalendar.getTime(), testTime);
        }


        [Test]
        public void testFinalFireTimes()
        {
            Calendar startCalendar = Calendar.getInstance();
            startCalendar.set(2010, Calendar.MARCH, 12, 9, 0, 0);
            startCalendar.clear(Calendar.MILLISECOND);

            CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.setStartTime(startCalendar.getTime());
            dailyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.DAY);
            dailyTrigger.setRepeatInterval(5); // every 5 days

            Calendar endCalendar = Calendar.getInstance();
            endCalendar.setTime(startCalendar.getTime());
            endCalendar.setLenient(true);
            endCalendar.add(Calendar.DAY_OF_YEAR, 10); // jump 10 days (2 intervals)
            endCalendar.clear(Calendar.MILLISECOND);
            dailyTrigger.setEndTime(endCalendar.getTime());

            Date testTime = dailyTrigger.getFinalFireTime();

            Assert.AreEqual("Final fire time not computed correctly for day interval.", endCalendar.getTime(), testTime);


            startCalendar = Calendar.getInstance();
            startCalendar.set(2010, Calendar.MARCH, 12, 9, 0, 0);
            startCalendar.clear(Calendar.MILLISECOND);

            dailyTrigger = new CalendarIntervalTriggerImpl();
            dailyTrigger.setStartTime(startCalendar.getTime());
            dailyTrigger.setRepeatIntervalUnit(DateBuilder.IntervalUnit.MINUTE);
            dailyTrigger.setRepeatInterval(5); // every 5 minutes

            endCalendar = Calendar.getInstance();
            endCalendar.setTime(startCalendar.getTime());
            endCalendar.setLenient(true);
            endCalendar.add(Calendar.DAY_OF_YEAR, 15); // jump 15 days 
            endCalendar.add(Calendar.MINUTE, -2); // back up two minutes
            endCalendar.clear(Calendar.MILLISECOND);
            dailyTrigger.setEndTime(endCalendar.getTime());

            testTime = dailyTrigger.getFinalFireTime();

            Assert.IsTrue("Final fire time not computed correctly for minutely interval.", (endCalendar.getTime().after(testTime)));

            endCalendar.add(Calendar.MINUTE, -3); // back up three more minutes

            Assert.IsTrue("Final fire time not computed correctly for minutely interval.", (endCalendar.getTime().equals(testTime)));
        }

        protected override object GetTargetObject()
        {
            JobDataMap jobDataMap = new JobDataMap();
            jobDataMap.put("A", "B");

            CalendarIntervalTriggerImpl t = new CalendarIntervalTriggerImpl();
            t.setName("test");
            t.setGroup("testGroup");
            t.setCalendarName("MyCalendar");
            t.setDescription("CronTriggerDesc");
            t.setJobDataMap(jobDataMap);
            t.setRepeatInterval(5);
            t.setRepeatIntervalUnit(IntervalUnit.DAY);

            return t;
        }


        protected override string[] GetVersions()
        {
            return VERSIONS;
        }

        protected override void VerifyMatch(object target, object deserialized)
        {
            CalendarIntervalTriggerImpl targetCalTrigger = (CalendarIntervalTriggerImpl) target;
            CalendarIntervalTriggerImpl deserializedCalTrigger = (CalendarIntervalTriggerImpl) deserialized;

            Assert.IsNotNull(deserializedCalTrigger);
            Assert.AreEqual(targetCalTrigger.getName(), deserializedCalTrigger.getName());
            Assert.AreEqual(targetCalTrigger.getGroup(), deserializedCalTrigger.getGroup());
            Assert.AreEqual(targetCalTrigger.getJobName(), deserializedCalTrigger.getJobName());
            Assert.AreEqual(targetCalTrigger.getJobGroup(), deserializedCalTrigger.getJobGroup());
//        assertEquals(targetCronTrigger.getStartTime(), deserializedCronTrigger.getStartTime());
            Assert.AreEqual(targetCalTrigger.getEndTime(), deserializedCalTrigger.getEndTime());
            Assert.AreEqual(targetCalTrigger.getCalendarName(), deserializedCalTrigger.getCalendarName());
            Assert.AreEqual(targetCalTrigger.getDescription(), deserializedCalTrigger.getDescription());
            Assert.AreEqual(targetCalTrigger.getJobDataMap(), deserializedCalTrigger.getJobDataMap());
            Assert.AreEqual(targetCalTrigger.getRepeatInterval(), deserializedCalTrigger.getRepeatInterval());
            Assert.AreEqual(targetCalTrigger.getRepeatIntervalUnit(), deserializedCalTrigger.getRepeatIntervalUnit());
        }
    }
}