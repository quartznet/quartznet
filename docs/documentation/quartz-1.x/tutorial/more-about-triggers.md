---

title: 'Lesson 4: More About Triggers'
---

Like jobs, triggers are relatively easy to work with, but do contain a variety of customizable options that you need to
be aware of and understand before you can make full use of Quartz.NET. Also, as noted earlier, there are different types of triggers,
that you can select to meet different scheduling needs.

## Calendars

Quartz Calendar objects can be associated with triggers at the time the trigger is stored in the scheduler.
Calendars are useful for excluding blocks of time from the the trigger's firing schedule. For instance, you could
create a trigger that fires a job every weekday at 9:30 am, but then add a Calendar that excludes all of the business's holidays.

Calendar's can be any serializable objects that implement the ICalendar interface, which looks like this:

```csharp
    namespace Quartz
    {
        public interface ICalendar
        {
            string Description { get; set; }
    
            ICalendar CalendarBase { set; get; }
    
            bool IsTimeIncluded(DateTime timeUtc);
    
            DateTime GetNextIncludedTimeUtc(DateTime timeUtc);
        }
    }
```

Notice that the parameters to these methods are of the long type. As you may guess, they are timestamps in millisecond format.
This means that calendars can 'block out' sections of time as narrow as a millisecond. Most likely, you'll be interested in
'blocking-out' entire days. As a convenience, Quartz includes the class HolidayCalendar, which does just that.

Calendars must be instantiated and registered with the scheduler via the AddCalendar(..) method. If you use HolidayCalendar,
after instantiating it, you should use its AddExcludedDate(DateTime date) method in order to populate it with the days you wish
to have excluded from scheduling. The same calendar instance can be used with multiple triggers such as this:

__Using Calendars__

```csharp
    HolidayCalendar cal = new HolidayCalendar();
    cal.AddExcludedDate(someDate);
    
    sched.AddCalendar("myHolidays", cal, false);
    
    // fire every one hour interval
    Trigger trigger = TriggerUtils.MakeHourlyTrigger();
    // start on the next even hour
    trigger.StartTimeUtc = TriggerUtils.GetEvenHourDate(DateTime.Now); 
    trigger.Name = "myTrigger1";
    
    trigger.CalendarName = "myHolidays";
    
    // .. schedule job with trigger
    
    // fire every day at 08:00
    Trigger trigger2 = TriggerUtils.MakeDailyTrigger(8, 0);
    // begin immediately
    trigger.StartTimeUtc = DateTime.UtcNow; 
    trigger2.Name = "myTrigger2";
    trigger2.CalendarName = "myHolidays";
    
    // .. schedule job with trigger2 
```

The details of the values passed in the SimpleTrigger constructors will be explained in the next section.
For now, just believe that the code above creates two triggers: one that will repeat every 60 seconds forever, and one that
will repeat five times with a five day interval between firings. However, any of the firings that would have
occurred during the period excluded by the calendar will be skipped.

## Priority

Sometimes, when you have many Triggers (or few worker threads in your Quartz.NET thread pool), Quartz.NET may not have enough resources to
immediately fire all of the Triggers that are scheduled to fire at the same time.  In this case, you may want to control
which of your Triggers get first crack at the available Quartz.NET worker threads.  For this purpose, you can set the priority property on a Trigger.
If N Triggers are to fire at the same time, but there are only Z worker threads currently available, then the first Z Triggers with the highest priority will get first dibs.
If you do not set a priority on a Trigger, then it will use the default priority of 5.
Any integer value is allowed for priority, positive or negative.

Note: When a Trigger is detected to require recovery, its recovery is scheduled with the same priority as the original Trigger.

__Priority Example__

```csharp
    // All three Triggers will be scheduled to fire 5 minutes from now.
    DateTime d = DateTime.UtcNow.AddMinutes(5);
    
    Trigger trig1 = new SimpleTrigger("T1", "MyGroup", d);
    Trigger trig2 = new SimpleTrigger("T2", "MyGroup", d);
    Trigger trig3 = new SimpleTrigger("T3", "MyGroup", d);
    
    JobDetail jobDetail = new JobDetail("MyJob", "MyGroup", typeof(NoOpJob));
    
    // Trigger1 does not have its priority set, so it defaults to 5
    sched.ScheduleJob(jobDetail, trig1);
    
    // Trigger2 has its priority set to 10
    trig2.JobName = jobDetail.Name;
    trig2.Priority = 10;
    sched.ScheduleJob(trig2);
    
    // Trigger2 has its priority set to 1
    trig3.JobName = jobDetail.Name;
    trig3.Priority = 1;
    sched.ScheduleJob(trig3);
    
    // Five minutes from now, when the scheduler invokes these three triggers
    // they will be allocated worker threads in decreasing order of their
    // priority: Trigger2(10), Trigger1(5), Trigger3(1) 
```

## Misfire Instructions

Another important property of a Trigger is its "misfire instruction". A misfire occurs if a persistent trigger "misses"
its firing time because of the scheduler being shutdown, or because there are no available threads in Quartz's thread pool for executing the job.
The different trigger types have different misfire instructions available to them. By default they use a 'smart policy' instruction

- which has dynamic behavior based on trigger type and configuration. When the scheduler starts, it searches for any persistent triggers that
have misfired, and it then updates each of them based on their individually configured misfire instructions.

When you start using Quartz in your
own projects, you should make yourself familiar with the misfire instructions that are defined on the given trigger types,
and explained in their API documentation. More specific information about misfire instructions will be given within
the tutorial lessons specific to each trigger type. The misfire instruction for a given trigger instance can be configured
using the MisfireInstruction property.

## TriggerUtils - Triggers Made Easy

The TriggerUtils class contains conveniences to help you create triggers and dates without
having to monkey around with DateTime objects. Use this class to easily make triggers that fire every minute,
hour, day, week, month, etc. Also use this class to generate dates that are rounded to the nearest second, minute or hour -
this can be very useful for setting trigger start-times.

## Trigger Listeners

Finally, triggers may have registered listeners, just as jobs may.
Objects implementing the ITriggerListener interface will receive notifications as a trigger is fired.
