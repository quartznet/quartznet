using FluentAssertions;
using FluentAssertions.Execution;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class CalendarIntervalTriggerTest : SerializationTestSupport<CalendarIntervalTriggerImpl>
{
    public CalendarIntervalTriggerTest(Type serializerType) : base(serializerType)
    {
    }

    [Test]
    public void TestYearlyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Year,
            RepeatInterval = 2 // every two years;
        };

        DateTimeOffset targetCalendar = startCalendar.AddYears(4);

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
        DateTimeOffset thirdTime = fireTimes[2]; // get the third fire time

        Assert.That(thirdTime, Is.EqualTo(targetCalendar), "Year increment result not as expected.");
    }

    [Test]
    public void TestMonthlyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Month,
            RepeatInterval = 5 // every five months
        };

        DateTimeOffset targetCalendar = startCalendar.AddMonths(25); // jump 25 five months (5 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
        DateTimeOffset sixthTime = fireTimes[5]; // get the sixth fire time

        Assert.That(sixthTime, Is.EqualTo(targetCalendar), "Month increment result not as expected.");
    }

    [Test]
    public void TestWeeklyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Week,
            RepeatInterval = 6 // every six weeks
        };

        DateTimeOffset targetCalendar = startCalendar.AddDays(7 * 6 * 4); // jump 24 weeks (4 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 7);
        DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

        Assert.That(fifthTime, Is.EqualTo(targetCalendar), "Week increment result not as expected.");
    }

    [Test]
    public void TestDailyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var dailyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Day,
            RepeatInterval = 90 // every ninety days
        };

        DateTimeOffset targetCalendar = startCalendar.AddDays(360); // jump 360 days (4 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
        DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

        Assert.That(fifthTime, Is.EqualTo(targetCalendar), "Day increment result not as expected.");
    }

    [Test]
    public void TestHourlyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Hour,
            RepeatInterval = 100 // every 100 hours
        };

        DateTimeOffset targetCalendar = startCalendar.AddHours(400); // jump 400 hours (4 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
        DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

        Assert.That(fifthTime, Is.EqualTo(targetCalendar), "Hour increment result not as expected.");
    }

    [Test]
    public void TestMinutelyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Minute,
            RepeatInterval = 100 // every 100 minutes
        };

        DateTimeOffset targetCalendar = startCalendar.AddMinutes(400); // jump 400 minutes (4 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
        DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

        Assert.That(fifthTime, Is.EqualTo(targetCalendar), "Minutes increment result not as expected.");
    }

    [Test]
    public void TestSecondlyIntervalGetFireTimeAfter()
    {
        DateTimeOffset startCalendar = DateBuilder.DateOf(9, 30, 17, 1, 6, 2005);

        var yearlyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Second,
            RepeatInterval = 100 // every 100 seconds
        };

        DateTimeOffset targetCalendar = startCalendar.AddSeconds(400); // jump 400 seconds (4 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
        DateTimeOffset fifthTime = fireTimes[4]; // get the third fire time

        Assert.That(fifthTime, Is.EqualTo(targetCalendar), "Seconds increment result not as expected.");
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
            RepeatInterval = 5 // every 5 days
        };

        DateTimeOffset targetCalendar = startCalendar.AddDays(10); // jump 10 days (2 intervals)

        var fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
        DateTimeOffset testTime = fireTimes[2]; // get the third fire time

        Assert.That(testTime, Is.EqualTo(targetCalendar), "Day increment result not as expected over spring 2010 daylight savings transition.");

        // And again, Pick a day before a spring daylight savings transition... (QTZ-240)

        startCalendar = new DateTime(2011, 3, 12, 1, 0, 0);

        dailyTrigger = new CalendarIntervalTriggerImpl();
        dailyTrigger.StartTimeUtc = startCalendar;
        dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
        dailyTrigger.RepeatInterval = 1; // every day

        targetCalendar = startCalendar.AddDays(2); // jump 2 days (2 intervals)

        fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
        testTime = fireTimes[2]; // get the third fire time

        Assert.That(testTime, Is.EqualTo(targetCalendar), "Day increment result not as expected over spring 2011 daylight savings transition.");

        // And again, Pick a day before a spring daylight savings transition... (QTZ-240) - and prove time of day is not preserved without setPreserveHourOfDayAcrossDaylightSavings(true)

        var cetTimeZone = TimeZoneUtil.FindTimeZoneById("Central European Standard Time");
        startCalendar = TimeZoneInfo.ConvertTime(new DateTime(2011, 3, 26, 4, 0, 0), cetTimeZone);

        dailyTrigger = new CalendarIntervalTriggerImpl();
        dailyTrigger.StartTimeUtc = startCalendar;
        dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
        dailyTrigger.RepeatInterval = 1; // every day

        targetCalendar = TimeZoneUtil.ConvertTime(startCalendar, cetTimeZone);
        targetCalendar = targetCalendar.AddDays(2); // jump 2 days (2 intervals)

        fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);

        testTime = fireTimes[2]; // get the third fire time

        DateTimeOffset testCal = TimeZoneUtil.ConvertTime(testTime, cetTimeZone);

        Assert.That(testCal.Hour, Is.Not.EqualTo(targetCalendar.Hour), "Day increment time-of-day result not as expected over spring 2011 daylight savings transition.");

        // And again, Pick a day before a spring daylight savings transition... (QTZ-240) - and prove time of day is preserved with setPreserveHourOfDayAcrossDaylightSavings(true)

        startCalendar = TimeZoneUtil.ConvertTime(new DateTime(2011, 3, 26, 4, 0, 0), cetTimeZone);

        dailyTrigger = new CalendarIntervalTriggerImpl();
        dailyTrigger.StartTimeUtc = startCalendar;
        dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
        dailyTrigger.RepeatInterval = 1; // every day
        dailyTrigger.TimeZone = cetTimeZone;
        dailyTrigger.PreserveHourOfDayAcrossDaylightSavings = true;

        targetCalendar = startCalendar.AddDays(2); // jump 2 days (2 intervals)

        fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);

        testTime = fireTimes[1]; // get the second fire time

        testCal = TimeZoneUtil.ConvertTime(testTime, cetTimeZone);

        Assert.That(testCal.Hour, Is.EqualTo(targetCalendar.Hour), "Day increment time-of-day result not as expected over spring 2011 daylight savings transition.");

        // Pick a day before a fall daylight savings transition...

        startCalendar = new DateTimeOffset(2010, 10, 31, 9, 30, 17, TimeSpan.Zero);

        dailyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Day,
            RepeatInterval = 5 // every 5 days
        };

        targetCalendar = startCalendar.AddDays(15); // jump 15 days (3 intervals)

        fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
        testTime = fireTimes[3]; // get the fourth fire time

        Assert.That(testTime, Is.EqualTo(targetCalendar), "Day increment result not as expected over fall daylight savings transition.");
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
            RepeatInterval = 5, // every 5 days
            EndTimeUtc = endCalendar
        };

        DateTimeOffset? testTime = dailyTrigger.FinalFireTimeUtc;

        Assert.That(testTime, Is.EqualTo(endCalendar), "Final fire time not computed correctly for day interval.");

        endCalendar = startCalendar.AddDays(15).AddMinutes(-2); // jump 15 days and back up 2 minutes
        dailyTrigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startCalendar,
            RepeatIntervalUnit = IntervalUnit.Minute,
            RepeatInterval = 5, // every 5 minutes
            EndTimeUtc = endCalendar
        };

        testTime = dailyTrigger.FinalFireTimeUtc;

        Assert.That(endCalendar, Is.GreaterThan(testTime), "Final fire time not computed correctly for minutely interval.");

        endCalendar = endCalendar.AddMinutes(-3); // back up three more minutes

        Assert.That(testTime, Is.EqualTo(endCalendar), "Final fire time not computed correctly for minutely interval.");
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

    [Test]
    public void TestTimeZoneTransition()
    {
        TimeZoneInfo timeZone = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl("trigger", IntervalUnit.Day, 1)
        {
            TimeZone = timeZone,
            StartTimeUtc = new DateTimeOffset(2012, 11, 2, 12, 0, 0, TimeSpan.FromHours(-4))
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 6);

        var expected = new DateTimeOffset(2012, 11, 2, 12, 0, 0, TimeSpan.FromHours(-4));
        Assert.That(fireTimes[0], Is.EqualTo(expected));

        expected = new DateTimeOffset(2012, 11, 3, 12, 0, 0, TimeSpan.FromHours(-4));
        Assert.That(fireTimes[1], Is.EqualTo(expected));

        //this next day should be a new daylight savings change, notice the change in offset
        expected = new DateTimeOffset(2012, 11, 4, 11, 0, 0, TimeSpan.FromHours(-5));
        Assert.That(fireTimes[2], Is.EqualTo(expected));

        expected = new DateTimeOffset(2012, 11, 5, 11, 0, 0, TimeSpan.FromHours(-5));
        Assert.That(fireTimes[3], Is.EqualTo(expected));
    }

    [Test]
    public void TestSkipDayIfItDoesNotExists()
    {
        TimeZoneInfo timeZone = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

        //March 11, 2012, EST DST starts at 2am and jumps to 3.
        // 3/11/2012 2:00:00 AM is an invalid time

        //-------------------------------------------------
        // DAILY
        //-------------------------------------------------
        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = true
        };

        DateTimeOffset startDate = new DateTimeOffset(2012, 3, 10, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        var fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        var targetTime = fires[1]; //get second fire

        Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");

        DateTimeOffset expectedTarget = new DateTimeOffset(2012, 3, 12, 2, 0, 0, TimeSpan.FromHours(-4)); // 3/12/2012 2am
        Assert.That(targetTime, Is.EqualTo(expectedTarget));

        //-------------------------------------------------
        // WEEKLY
        //-------------------------------------------------
        trigger = new CalendarIntervalTriggerImpl();
        trigger.TimeZone = timeZone;
        trigger.RepeatInterval = 1;
        trigger.RepeatIntervalUnit = IntervalUnit.Week;
        trigger.PreserveHourOfDayAcrossDaylightSavings = true;
        trigger.SkipDayIfHourDoesNotExist = true;

        startDate = new DateTimeOffset(2012, 3, 4, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");

        expectedTarget = new DateTimeOffset(2012, 3, 18, 2, 0, 0, TimeSpan.FromHours(-4)); // 3/18/2012 2am
        Assert.That(targetTime, Is.EqualTo(expectedTarget));

        //-------------------------------------------------
        // MONTHLY
        //-------------------------------------------------

        trigger = new CalendarIntervalTriggerImpl();
        trigger.TimeZone = timeZone;
        trigger.RepeatInterval = 1;
        trigger.RepeatIntervalUnit = IntervalUnit.Month;
        trigger.PreserveHourOfDayAcrossDaylightSavings = true;
        trigger.SkipDayIfHourDoesNotExist = true;

        startDate = new DateTimeOffset(2012, 2, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");

        expectedTarget = new DateTimeOffset(2012, 4, 11, 2, 0, 0, TimeSpan.FromHours(-4)); // 4/11/2012 2am
        Assert.That(targetTime, Is.EqualTo(expectedTarget));

        //-------------------------------------------------
        // YEARLY
        //-------------------------------------------------

        trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Year,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = true
        };

        startDate = new DateTimeOffset(2011, 3, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");

        expectedTarget = new DateTimeOffset(2013, 3, 11, 2, 0, 0, TimeSpan.FromHours(-4)); // 3/11/2013 2am
        Assert.That(targetTime, Is.EqualTo(expectedTarget));
    }

    [Test]
    public void TestSkipDayIfItDoesNotExistsIsFalse()
    {
        TimeZoneInfo timeZone = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

        //March 11, 2012, EST DST starts at 2am and jumps to 3.
        // 3/11/2012 2:00:00 AM is an invalid time

        //expected target will always be the on the next valid time, (3/11/2012 3am) in this case
        DateTimeOffset expectedTarget = new DateTimeOffset(2012, 3, 11, 3, 0, 0, TimeSpan.FromHours(-4));

        //-------------------------------------------------
        // DAILY
        //-------------------------------------------------

        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false
        };

        DateTimeOffset startDate = new DateTimeOffset(2012, 3, 10, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        var fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        var targetTime = fires[1]; //get second fire

        Assert.Multiple(() =>
        {
            Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");
            Assert.That(targetTime, Is.EqualTo(expectedTarget));
        });

        //-------------------------------------------------
        // WEEKLY
        //-------------------------------------------------
        trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Week,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false
        };

        startDate = new DateTimeOffset(2012, 3, 4, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.Multiple(() =>
        {
            Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");
            Assert.That(targetTime, Is.EqualTo(expectedTarget));
        });

        //-------------------------------------------------
        // MONTHLY
        //-------------------------------------------------
        trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Month,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false
        };

        startDate = new DateTimeOffset(2012, 2, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.Multiple(() =>
        {
            Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");
            Assert.That(targetTime, Is.EqualTo(expectedTarget));
        });

        //-------------------------------------------------
        // YEARLY
        //-------------------------------------------------

        trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = timeZone,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Year,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false
        };

        startDate = new DateTimeOffset(2011, 3, 11, 2, 0, 0, 0, TimeSpan.FromHours(-5));
        trigger.StartTimeUtc = startDate;

        fires = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        targetTime = fires[1]; //get second fire

        Assert.Multiple(() =>
        {
            Assert.That(timeZone.IsInvalidTime(targetTime.DateTime), Is.False, "did not seem to skip the day with an hour that doesn't exist.");
            Assert.That(targetTime, Is.EqualTo(expectedTarget));
        });
    }

    [Test]
    public void TestStartTimeOnDayInDifferentOffset()
    {
        TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        DateTimeOffset startDate = new DateTimeOffset(2012, 3, 11, 12, 0, 0, TimeSpan.FromHours(-5));

        CalendarIntervalTriggerImpl t = new CalendarIntervalTriggerImpl
        {
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate,
            TimeZone = est
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(t, null, 10);

        var firstFire = fireTimes[0];
        var secondFire = fireTimes[1];

        Assert.That(secondFire, Is.Not.EqualTo(firstFire));
    }

    [Test]
    public void TestMovingAcrossDSTAvoidsInfiniteLoop()
    {
        TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        DateTimeOffset startDate = new DateTimeOffset(1990, 10, 27, 0, 0, 0, TimeSpan.FromHours(-4));

        CalendarIntervalTriggerImpl t = new CalendarIntervalTriggerImpl
        {
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate,
            TimeZone = est
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(t, null, 10);

        var firstFire = fireTimes[0];
        var secondFire = fireTimes[1];
        Assert.That(secondFire, Is.Not.EqualTo(firstFire));

        //try to trigger a shift in month
        startDate = new DateTimeOffset(2012, 6, 1, 0, 0, 0, TimeSpan.FromHours(-4));

        t = new CalendarIntervalTriggerImpl
        {
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Month,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate,
            TimeZone = est
        };

        fireTimes = TriggerUtils.ComputeFireTimes(t, null, 10);

        Assert.That(secondFire, Is.Not.EqualTo(firstFire));
    }

    [Test]
    public void TestCrossingDSTBoundary()
    {
        TimeZoneInfo cetTimeZone = TimeZoneUtil.FindTimeZoneById("Central European Standard Time");
        DateTimeOffset startCalendar = TimeZoneUtil.ConvertTime(new DateTime(2011, 3, 26, 4, 0, 0), cetTimeZone);

        CalendarIntervalTriggerImpl dailyTrigger = new CalendarIntervalTriggerImpl();
        dailyTrigger.StartTimeUtc = startCalendar;
        dailyTrigger.RepeatIntervalUnit = IntervalUnit.Day;
        dailyTrigger.RepeatInterval = 1;
        dailyTrigger.TimeZone = cetTimeZone;
        dailyTrigger.PreserveHourOfDayAcrossDaylightSavings = true;

        var fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);

        //none of these should match the previous fire time.
        for (int i = 1; i < fireTimes.Count; i++)
        {
            var previousFire = fireTimes[i - 1];
            var currentFire = fireTimes[i];

            Assert.That(currentFire, Is.Not.EqualTo(previousFire));
        }
    }

    [Test]
    public void TestPreserveHourOfDayAcrossDaylightSavingsNotHanging()
    {
        TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

        DateTimeOffset startTime = new DateTimeOffset(2013, 3, 1, 4, 0, 0, TimeSpan.FromHours(-5));

        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl();
        trigger.RepeatInterval = 1;
        trigger.RepeatIntervalUnit = IntervalUnit.Day;
        trigger.TimeZone = est;
        trigger.StartTimeUtc = startTime;
        trigger.PreserveHourOfDayAcrossDaylightSavings = true;

        DateTimeOffset? fireTimeAfter = new DateTimeOffset(2013, 3, 10, 4, 0, 0, TimeSpan.FromHours(-4));

        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(fireTimeAfter);

        Assert.Multiple(() =>
        {
            Assert.That(fireTimeAfter, Is.Not.EqualTo(fireTime));
            Assert.That(fireTime, Is.GreaterThan(fireTimeAfter));
        });
    }

    [Test]
    public void ShouldCreateCorrectFiringsWhenPreservingHourAcrossDaylightSavings()
    {
        var tb = TriggerBuilder.Create();
        var schedBuilder = CalendarIntervalScheduleBuilder.Create();

        schedBuilder.WithIntervalInWeeks(2);
        schedBuilder
            .PreserveHourOfDayAcrossDaylightSavings(true)
            .WithMisfireHandlingInstructionFireAndProceed();

        var trigger = tb.StartAt(new DateTimeOffset(new DateTime(2014, 2, 26, 23, 45, 0)))
            .WithSchedule(schedBuilder)
            .Build();

        DateTimeOffset? fireTime = null;
        var prevTime = new DateTimeOffset(DateTime.UtcNow);
        for (int i = 0; i < 100; ++i)
        {
            fireTime = trigger.GetFireTimeAfter(fireTime);
            if (fireTime is null)
            {
                break;
            }

            var timeSpan = fireTime.Value - prevTime;

            /*
            Console.WriteLine("{0}: At {1:yyyy-MM-dd HH:mm:ss} ({2:ddd}) in {3}", i, fireTime.Value.LocalDateTime,
                fireTime.Value.LocalDateTime, fireTime.Value - prevTime);
            */
            if (i > 1)
            {
                Assert.That((int) timeSpan.TotalDays, Is.GreaterThanOrEqualTo(13), "should have had more than 13 days between");
                Assert.That((int) timeSpan.TotalDays, Is.LessThanOrEqualTo(15), "should have had less than 15 days between");
            }

            prevTime = fireTime.Value;
        }
    }

    [Test]
    public void ShouldGetScheduleBuilderWithSameSettingsAsTrigger()
    {
        var startTime = DateTimeOffset.UtcNow;
        var endTime = DateTimeOffset.UtcNow.AddDays(1);
        var trigger = new CalendarIntervalTriggerImpl("name", "group", startTime, endTime, IntervalUnit.Hour, 10);
        trigger.PreserveHourOfDayAcrossDaylightSavings = true;
        trigger.SkipDayIfHourDoesNotExist = true;
        trigger.TimeZone = TimeZoneInfo.Utc;
        trigger.MisfireInstruction = MisfireInstruction.CalendarIntervalTrigger.FireOnceNow;
        var scheduleBuilder = trigger.GetScheduleBuilder();

        var cloned = (CalendarIntervalTriggerImpl) scheduleBuilder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(cloned.PreserveHourOfDayAcrossDaylightSavings, Is.EqualTo(trigger.PreserveHourOfDayAcrossDaylightSavings));
            Assert.That(cloned.SkipDayIfHourDoesNotExist, Is.EqualTo(trigger.SkipDayIfHourDoesNotExist));
            Assert.That(cloned.RepeatInterval, Is.EqualTo(trigger.RepeatInterval));
            Assert.That(cloned.RepeatIntervalUnit, Is.EqualTo(trigger.RepeatIntervalUnit));
            Assert.That(cloned.MisfireInstruction, Is.EqualTo(trigger.MisfireInstruction));
            Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
        });
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/505")]
    public void ShouldRespectTimeZoneForFirstFireTime()
    {
        var tz = TZConvert.GetTimeZoneInfo("E. South America Standard Time");
        var dailyTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .StartAt(new DateTime(2017, 1, 4, 15, 0, 0, DateTimeKind.Utc))
            .WithCalendarIntervalSchedule(x => x
                .WithIntervalInDays(2)
                .InTimeZone(tz))
            .Build();

        var firstFireTime = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 1).First();
        Assert.That(firstFireTime, Is.EqualTo(new DateTimeOffset(2017, 1, 4, 13, 0, 0, TimeSpan.FromHours(-2))));
    }

    [Test]
    public void TriggerBuilderShouldHandleIgnoreMisfirePolicy()
    {
        var trigger1 = TriggerBuilder.Create()
            .WithCalendarIntervalSchedule(x => x
                .WithMisfireHandlingInstructionIgnoreMisfires()
            )
            .Build();

        var trigger2 = trigger1
            .GetTriggerBuilder()
            .Build();
        using (new AssertionScope())
        {
            trigger1.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
            trigger2.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
        }
    }

    [Test]
    [Description("CalendarIntervalSchedule firing non-stop during spring-forward DST transition")]
    public void TestWeeklyTriggerDoesNotFireInfinitelyDuringSpringForward()
    {
        // Reproduce the exact scenario from the issue:
        // - Every other Sunday at 2:01 AM Central
        // - Starts on 2/25/2024 2:01 AM
        // - Should fire at 2:01 AM on 3/10/2024 (but that time doesn't exist - spring forward)
        // - Quartz correctly sets next fire to 3/10/2024 3:00 AM
        // - When 3:00 AM comes around, the trigger should fire ONCE and compute the next fire time correctly
        var centralTimeZone = TimeZoneUtil.FindTimeZoneById("Central Standard Time");

        // 2/25/2024 2:01 AM CST (UTC-6)
        var startDate = new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = centralTimeZone,
            RepeatInterval = 2, // every 2 weeks
            RepeatIntervalUnit = IntervalUnit.Week,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false, // Don't skip, fire at next valid time
            StartTimeUtc = startDate
        };

        // Compute the first few fire times using TriggerUtils.ComputeFireTimes
        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 10);

        // Expected fire times:
        // 1. 2/25/2024 2:01 AM CST
        // 2. 3/10/2024 3:00 AM CDT (2:01 AM doesn't exist - DST springs forward at 2:00 AM, so next valid time is 3:00 AM)
        // 3. 3/24/2024 2:01 AM CDT
        // 4. 4/7/2024 2:01 AM CDT
        // ... and so on

        Assert.That(fireTimes, Is.Not.Null);
        Assert.That(fireTimes.Count, Is.GreaterThanOrEqualTo(4));

        // First fire: 2/25/2024 2:01 AM CST
        var firstFire = fireTimes[0];
        Assert.That(firstFire, Is.EqualTo(new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6))));

        // Second fire: 3/10/2024 - DST transition day
        // 2:01 AM doesn't exist (DST springs forward from 2:00 AM to 3:00 AM), so it should fire at 3:00 AM CDT
        var secondFire = fireTimes[1];
        Assert.That(secondFire, Is.EqualTo(new DateTimeOffset(2024, 3, 10, 3, 0, 0, TimeSpan.FromHours(-5))));

        // Third fire: 3/24/2024 2:01 AM CDT (normal firing, no DST transition)
        var thirdFire = fireTimes[2];
        Assert.That(thirdFire, Is.EqualTo(new DateTimeOffset(2024, 3, 24, 2, 1, 0, TimeSpan.FromHours(-5))));

        // Fourth fire: 4/7/2024 2:01 AM CDT
        var fourthFire = fireTimes[3];
        Assert.That(fourthFire, Is.EqualTo(new DateTimeOffset(2024, 4, 7, 2, 1, 0, TimeSpan.FromHours(-5))));

        // Critical test: Verify that all fire times are strictly increasing
        for (var i = 1; i < fireTimes.Count; i++)
        {
            Assert.That(fireTimes[i] > fireTimes[i - 1], Is.True,
                $"Fire time {i} ({fireTimes[i]}) should be after fire time {i - 1} ({fireTimes[i - 1]})");
        }

        // Verify that calling GetFireTimeAfter from the second fire time does NOT return the same time (which would cause infinite loop)
        var nextAfterSecond = trigger.GetFireTimeAfter(secondFire);
        Assert.That(nextAfterSecond, Is.Not.Null);
        Assert.That(nextAfterSecond > secondFire, Is.True,
            $"Next fire time after DST transition ({nextAfterSecond}) should be after the DST fire time ({secondFire})");

        // Also verify that the time delta between fires is reasonable (should be ~2 weeks)
        for (var i = 1; i < Math.Min(4, fireTimes.Count); i++)
        {
            var delta = fireTimes[i] - fireTimes[i - 1];
            Assert.That(delta.TotalDays, Is.InRange(13.5, 14.5),
                $"Time between fire {i - 1} and {i} should be approximately 2 weeks, but was {delta.TotalDays} days");
        }
    }

    [Test]
    [Description("Simplified test that demonstrates the infinite loop bug during spring-forward")]
    public void TestWeeklyTriggerInfiniteLoopBugDuringSpringForward()
    {
        // This test demonstrates the bug: when a CalendarIntervalTrigger with a weekly interval
        // encounters a DST spring-forward transition where the scheduled time doesn't exist,
        // GetFireTimeAfter() returns the same time repeatedly, causing an infinite loop.
        var centralTimeZone = TimeZoneUtil.FindTimeZoneById("Central Standard Time");

        // Start 2 weeks before DST (2/25/2024 2:01 AM CST)
        var startDate = new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = centralTimeZone,
            RepeatInterval = 2, // every 2 weeks
            RepeatIntervalUnit = IntervalUnit.Week,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate
        };

        // Simulate the sequence of fire times
        // First fire: 2/25/2024 2:01 AM CST
        var firstFire = new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6));

        // Second fire: 3/10/2024 (DST transition day) - 2:01 AM doesn't exist, advances to 3:00 AM
        var secondFire = trigger.GetFireTimeAfter(firstFire);
        Assert.That(secondFire, Is.EqualTo(new DateTimeOffset(2024, 3, 10, 3, 0, 0, TimeSpan.FromHours(-5))),
            "Second fire should be at 3:00 AM on DST transition day");

        // Historically, a bug caused the third fire time (expected 3/24/2024 2:01 AM) to be the SAME as the second fire time.
        var thirdFire = trigger.GetFireTimeAfter(secondFire);

        // This assertion verifies that the third fire time advances correctly and guards against regressions to that bug.
        Assert.That(thirdFire, Is.Not.EqualTo(secondFire),
            "Regression: GetFireTimeAfter returned the same fire time as the previous one, which would cause an infinite loop when iterating fire times.");

        Assert.That(thirdFire > secondFire, Is.True,
            "Third fire time should be after second fire time");

        // The expected third fire time is:
        Assert.That(thirdFire, Is.EqualTo(new DateTimeOffset(2024, 3, 24, 2, 1, 0, TimeSpan.FromHours(-5))),
            "Third fire should be 2 weeks after second fire, at 2:01 AM");
    }

    [Test]
    [Description("Variant test with SkipDayIfHourDoesNotExist=true during spring-forward")]
    public void TestWeeklyTriggerSkipsDayDuringSpringForward()
    {
        var centralTimeZone = TimeZoneUtil.FindTimeZoneById("Central Standard Time");

        // Start 2 weeks before DST transition
        var startDate = new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = centralTimeZone,
            RepeatInterval = 2, // every 2 weeks
            RepeatIntervalUnit = IntervalUnit.Week,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = true, // Skip the day entirely
            StartTimeUtc = startDate
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        Assert.That(fireTimes, Is.Not.Null);
        Assert.That(fireTimes.Count, Is.GreaterThanOrEqualTo(4));

        // First fire: 2/25/2024 2:01 AM CST
        var firstFire = fireTimes[0];
        Assert.That(firstFire, Is.EqualTo(new DateTimeOffset(2024, 2, 25, 2, 1, 0, TimeSpan.FromHours(-6))));

        // Second fire: Should SKIP 3/10/2024 entirely and fire on 3/24/2024
        var secondFire = fireTimes[1];
        Assert.That(secondFire, Is.EqualTo(new DateTimeOffset(2024, 3, 24, 2, 1, 0, TimeSpan.FromHours(-5))));

        // Verify all times are strictly increasing
        for (var i = 1; i < fireTimes.Count; i++)
        {
            Assert.That(fireTimes[i] > fireTimes[i - 1], Is.True,
                $"Fire time {i} should be after fire time {i - 1}");
        }
    }

    [Test]
    [Description("Recursion depth guard should respect EndTimeUtc")]
    public void TestRecursionDepthGuardRespectsEndTime()
    {
        // Test that the recursion-depth safety guard (which triggers after 10+ recursive calls)
        // still respects the trigger's EndTimeUtc instead of returning a time past the end time.

        var startDate = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var endDate = startDate.AddDays(30); // End time is 30 days after start

        var trigger = new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startDate,
            EndTimeUtc = endDate,
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day
        };

        var getFireTimeAfterWithDepth = typeof(CalendarIntervalTriggerImpl).GetMethod(
            "GetFireTimeAfter",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(DateTimeOffset?), typeof(bool), typeof(int) },
            modifiers: null);

        Assert.That(getFireTimeAfterWithDepth, Is.Not.Null);

        // Call GetFireTimeAfter with a high recursion depth (> 10) to trigger the guard
        // The afterTime is close to the end time
        var afterTime = endDate.AddDays(-5);

        // With recursionDepth > 10, the fallback logic adds 2 intervals (2 days)
        // afterTime + 2 days would be endDate - 3 days, which is still before endDate
        var fireTime = (DateTimeOffset?) getFireTimeAfterWithDepth!.Invoke(trigger, new object[] { afterTime, false, 11 });

        // Should return a valid time before the end date
        Assert.That(fireTime, Is.Not.Null);
        Assert.That(fireTime < endDate, Is.True);

        // Now test when the fallback would exceed the end time
        afterTime = endDate.AddDays(-1); // 1 day before end

        // With recursionDepth > 10, the fallback adds 2 days: afterTime + 2 days = endDate + 1 day
        // This exceeds EndTimeUtc, so it should return null
        fireTime = (DateTimeOffset?) getFireTimeAfterWithDepth.Invoke(trigger, new object[] { afterTime, false, 11 });

        Assert.That(fireTime, Is.Null, "Recursion guard should return null when fallback time exceeds EndTimeUtc");

        // Verify that with ignoreEndTime=true, it returns a time even past the end
        fireTime = (DateTimeOffset?) getFireTimeAfterWithDepth.Invoke(trigger, new object[] { afterTime, true, 11 });
        Assert.That(fireTime, Is.Not.Null);
    }

    [Test]
    [Description("Daily interval trigger should not loop infinitely during spring-forward DST transition")]
    public void TestDailyTriggerDoesNotFireInfinitelyDuringSpringForward()
    {
        var centralTimeZone = TimeZoneUtil.FindTimeZoneById("Central Standard Time");

        // Start a few days before DST spring-forward (3/10/2024)
        var startDate = new DateTimeOffset(2024, 3, 7, 2, 1, 0, TimeSpan.FromHours(-6));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = centralTimeZone,
            RepeatInterval = 1, // every day
            RepeatIntervalUnit = IntervalUnit.Day,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 10);

        Assert.That(fireTimes, Is.Not.Null);
        Assert.That(fireTimes.Count, Is.GreaterThanOrEqualTo(6));

        // 3/7 2:01 AM CST
        Assert.That(fireTimes[0], Is.EqualTo(new DateTimeOffset(2024, 3, 7, 2, 1, 0, TimeSpan.FromHours(-6))));
        // 3/8 2:01 AM CST
        Assert.That(fireTimes[1], Is.EqualTo(new DateTimeOffset(2024, 3, 8, 2, 1, 0, TimeSpan.FromHours(-6))));
        // 3/9 2:01 AM CST
        Assert.That(fireTimes[2], Is.EqualTo(new DateTimeOffset(2024, 3, 9, 2, 1, 0, TimeSpan.FromHours(-6))));
        // 3/10 DST day: 2:01 AM does not exist, fire at 3:00 AM CDT
        Assert.That(fireTimes[3], Is.EqualTo(new DateTimeOffset(2024, 3, 10, 3, 0, 0, TimeSpan.FromHours(-5))));
        // 3/11 2:01 AM CDT (back to normal)
        Assert.That(fireTimes[4], Is.EqualTo(new DateTimeOffset(2024, 3, 11, 2, 1, 0, TimeSpan.FromHours(-5))));
        // 3/12 2:01 AM CDT
        Assert.That(fireTimes[5], Is.EqualTo(new DateTimeOffset(2024, 3, 12, 2, 1, 0, TimeSpan.FromHours(-5))));

        // All fire times must be strictly increasing
        for (var i = 1; i < fireTimes.Count; i++)
        {
            Assert.That(fireTimes[i] > fireTimes[i - 1], Is.True,
                $"Fire time {i} ({fireTimes[i]}) should be after fire time {i - 1} ({fireTimes[i - 1]})");
        }
    }

    [Test]
    [Description("Monthly interval trigger should not loop infinitely during spring-forward DST transition")]
    public void TestMonthlyTriggerDoesNotFireInfinitelyDuringSpringForward()
    {
        var centralTimeZone = TimeZoneUtil.FindTimeZoneById("Central Standard Time");

        // Start on 1/10/2024 at 2:01 AM CST - the March fire lands on 3/10 (DST day)
        var startDate = new DateTimeOffset(2024, 1, 10, 2, 1, 0, TimeSpan.FromHours(-6));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = centralTimeZone,
            RepeatInterval = 1, // every month
            RepeatIntervalUnit = IntervalUnit.Month,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 6);

        Assert.That(fireTimes, Is.Not.Null);
        Assert.That(fireTimes.Count, Is.GreaterThanOrEqualTo(5));

        // 1/10 2:01 AM CST
        Assert.That(fireTimes[0], Is.EqualTo(new DateTimeOffset(2024, 1, 10, 2, 1, 0, TimeSpan.FromHours(-6))));
        // 2/10 2:01 AM CST
        Assert.That(fireTimes[1], Is.EqualTo(new DateTimeOffset(2024, 2, 10, 2, 1, 0, TimeSpan.FromHours(-6))));
        // 3/10 DST day: 2:01 AM does not exist, fire at 3:00 AM CDT
        Assert.That(fireTimes[2], Is.EqualTo(new DateTimeOffset(2024, 3, 10, 3, 0, 0, TimeSpan.FromHours(-5))));
        // 4/10 2:01 AM CDT (DST already in effect, 2:01 AM is valid)
        Assert.That(fireTimes[3], Is.EqualTo(new DateTimeOffset(2024, 4, 10, 2, 1, 0, TimeSpan.FromHours(-5))));
        // 5/10 2:01 AM CDT
        Assert.That(fireTimes[4], Is.EqualTo(new DateTimeOffset(2024, 5, 10, 2, 1, 0, TimeSpan.FromHours(-5))));

        // All fire times must be strictly increasing
        for (var i = 1; i < fireTimes.Count; i++)
        {
            Assert.That(fireTimes[i] > fireTimes[i - 1], Is.True,
                $"Fire time {i} ({fireTimes[i]}) should be after fire time {i - 1} ({fireTimes[i - 1]})");
        }
    }

    [Test]
    [Description("Weekly trigger with EU timezone should not loop infinitely during spring-forward DST transition")]
    public void TestWeeklyTriggerDoesNotFireInfinitelyDuringEuropeanSpringForward()
    {
        // EU DST spring-forward: last Sunday of March, clocks go from 2:00 AM to 3:00 AM CET to CEST
        // In 2024, this is March 31
        var cetTimeZone = TimeZoneUtil.FindTimeZoneById("Central European Standard Time");

        // Start 2 weeks before EU DST (3/17/2024 2:01 AM CET)
        var startDate = new DateTimeOffset(2024, 3, 17, 2, 1, 0, TimeSpan.FromHours(1));

        var trigger = new CalendarIntervalTriggerImpl
        {
            TimeZone = cetTimeZone,
            RepeatInterval = 2, // every 2 weeks
            RepeatIntervalUnit = IntervalUnit.Week,
            PreserveHourOfDayAcrossDaylightSavings = true,
            SkipDayIfHourDoesNotExist = false,
            StartTimeUtc = startDate
        };

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 5);

        Assert.That(fireTimes, Is.Not.Null);
        Assert.That(fireTimes.Count, Is.GreaterThanOrEqualTo(4));

        // 3/17 2:01 AM CET (+01:00)
        Assert.That(fireTimes[0], Is.EqualTo(new DateTimeOffset(2024, 3, 17, 2, 1, 0, TimeSpan.FromHours(1))));
        // 3/31 DST day: 2:01 AM does not exist, fire at 3:00 AM CEST (+02:00)
        Assert.That(fireTimes[1], Is.EqualTo(new DateTimeOffset(2024, 3, 31, 3, 0, 0, TimeSpan.FromHours(2))));
        // 4/14 2:01 AM CEST (+02:00) - back to normal
        Assert.That(fireTimes[2], Is.EqualTo(new DateTimeOffset(2024, 4, 14, 2, 1, 0, TimeSpan.FromHours(2))));

        // All fire times must be strictly increasing
        for (var i = 1; i < fireTimes.Count; i++)
        {
            Assert.That(fireTimes[i] > fireTimes[i - 1], Is.True,
                $"Fire time {i} ({fireTimes[i]}) should be after fire time {i - 1} ({fireTimes[i - 1]})");
        }
    }

    protected override CalendarIntervalTriggerImpl GetTargetObject()
    {
        var jobDataMap = new JobDataMap();
        jobDataMap["A"] = "B";

        var t = new CalendarIntervalTriggerImpl
        {
            Key = new TriggerKey("test", "testGroup"),
            CalendarName = "MyCalendar",
            Description = "CronTriggerDesc",
            JobDataMap = jobDataMap,
            RepeatInterval = 5,
            RepeatIntervalUnit = IntervalUnit.Day
        };

        return t;
    }

    protected override void VerifyMatch(CalendarIntervalTriggerImpl original, CalendarIntervalTriggerImpl deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Key, Is.EqualTo(original.Key));
            Assert.That(deserialized.JobKey, Is.EqualTo(original.JobKey));
            Assert.That(deserialized.StartTimeUtc, Is.EqualTo(original.StartTimeUtc).Within(TimeSpan.FromSeconds(1)));
            Assert.That(deserialized.EndTimeUtc, Is.EqualTo(original.EndTimeUtc));
            Assert.That(deserialized.CalendarName, Is.EqualTo(original.CalendarName));
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.JobDataMap, Is.EqualTo(original.JobDataMap));
            Assert.That(deserialized.RepeatInterval, Is.EqualTo(original.RepeatInterval));
            Assert.That(deserialized.RepeatIntervalUnit, Is.EqualTo(original.RepeatIntervalUnit));
        });
    }

    [Test]
    public void DoNothing_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new CalendarIntervalTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = startTime,
            RepeatInterval = 2,
            RepeatIntervalUnit = IntervalUnit.Minute,
            MisfireInstruction = MisfireInstruction.CalendarIntervalTrigger.DoNothing
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null, threshold);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
            "Should preserve the 10:02 fire time that is within the misfire threshold");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
