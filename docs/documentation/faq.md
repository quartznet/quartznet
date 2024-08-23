---

title: Frequently Asked Questions
sidebarDepth: 0
---

# FAQ

::: tip
This FAQ was adapted from Quartz Java
:::

# General Questions

## What is Quartz

Quartz is a job scheduling system that can be integrated with, or used along
side virtually any other software system. The term "job scheduler" seems to
conjure different ideas for different people. As you read this tutorial, you
should be able to get a firm idea of what we mean when we use this term, but
in short, a job scheduler is a system that is responsible for executing
(or notifying) other software components when a pre-determined (scheduled)
time arrives.

Quartz is quite flexible, and contains multiple usage paradigms that can be
used separately or together, in order to achieve your desired behavior, and
enable you to write your code in the manner that seems most 'natural' to
your project.

Quartz is very light-weight, and requires very little setup/configuration -
it can actually be used 'out-of-the-box' if your needs are relatively basic.

Quartz is fault-tolerant, and can persist ('remember') your scheduled
jobs between system restarts.

Although Quartz is extremely useful for simply running certain system
processes on given schedules, the full potential of Quartz can be realized
when you learn how to use it to drive the flow of your application's
business processes.

## What is Quartz - From a Software Component View?

Quartz is distributed as a small dynamically linked library (.dll file)
that contains all  of the core Quartz functionality. The main interface (API) to this
functionality is the Scheduler interface. It provides simple operations
such as scheduling/unscheduling jobs, starting/stopping/pausing the scheduler.

If you wish to schedule your own software components for execution they must
implement the simple Job interface, which contains the method execute().
If you wish to have components notified when a scheduled fire-time arrives,
then the components should implement either the TriggerListener or JobListener
interface.

The main Quartz 'process' can be started and ran within your own application,
or a stand-alone application (with an remote interface).

# Why not just use System.Timers.Timer?

.NET Framework has "built-in" timer capabilities, through the
System.Timers.Timer class - why would someone use Quartz rather than these
standard features?

There are many reasons! Here are a few:

* Timers have no persistence mechanism.
* Timers have inflexible scheduling (only able to set start-time & repeat interval, nothing based on dates, time of day, etc.
* Timers don't utilize a thread-pool (one thread per timer)
* Timers have no real management schemes - you'd have to write your own mechanism for being able to remember, organize and retrieve your tasks by name, etc.

...of course to some simple applications these features may not be important,
in which case it may then be the right decision not to use Quartz.NET.

# Miscellaneous Questions

## How many jobs is Quartz capable of running?

This is a tough question to answer... the answer is basically "it depends".

I know you hate that answer, so here's some information about what it depends "on".

First off, the JobStore that you use plays a significant factor.
The RAM-based JobStore is MUCH (1000x) faster than the ADO.NET-based JobStore.
The speed of AdoJobStore depends almost entirely on the speed of the
connection to your database, which data base system that you use, and what
hardware the database is running on. Quartz actually does very little
processing itself, nearly all of the time is spent in the database. Of course
RAMJobStore has a more finite limit on how many Jobs & Triggers can be stored,
as you're sure to have less RAM than hard-drive space for a database.
You may also look at the FAQ "How do I improve the performance of AdoJobStore?"

So, the limiting factor of the number of Triggers and Jobs Quartz can "store"
and monitor is really the amount of storage space available to the JobStore
(either the amount of RAM or the amount of disk space).

Now, aside from "how many can I store?" is the question of "how many jobs
can Quartz be running at the same moment in time?"

One thing that CAN slow down Quartz itself is using a lot of listeners
(TriggerListeners, JobListeners, and SchedulerListeners). The time spent in
each listener obviously adds into the time spent "processing" a job's
execution, outside of actual execution of the job. This doesn't mean that
you should be terrified of using listeners, it just means that you should
use them judiciously - don't create a bunch of "global" listeners if you can
really make more specialized ones. Also don't do "expensive" things in the
listeners, unless you really need to. Also be mindful that many
plug-ins (such as the "history" plugin) are actually listeners.

The actual number of jobs that can be running at any moment in time is
limited by the size of the thread pool. If there are five threads in
the pool, no more than five jobs can run at a time. Be careful of making a
lot of threads though, as the VM, Operating System, and CPU all have a hard
time juggling lots of threads, and performance degrades just because of all
of the management. In most cases performance starts to tank as you get into
the hundreds of threads. Be mindful that if you're running within an
application server, it probably has created at least a few dozen threads
of its own!

Aside from those factors, it really comes down to what your jobs DO.
If your jobs take a long time to complete their work, and/or their work is
very CPU-intensive, then you're obviously not going to be able to run very
many jobs at once, nor very many in a given spanse of time.

Finally, if you just can't get enough horse-power out of one Quartz instance,
you can always load-balance many Quartz instances (on separate machines).
Each will run the jobs out of the shared database on a first-come first-serve
basis, as quickly as the triggers need fired.

So here you are this far into the answer of "how many", and I still
haven't given you a number And I really hate to, because of all of the
variables mentioned above. So let me just say, there are installments of
Quartz Java out there that are managing hundreds-of-thousands of Jobs and Triggers,
and that at any given moment in time are executing dozens of jobs - and this
excludes using load-balancing. With this in mind, most people should feel
confident that they can get the performance out of Quartz that they need.

# Questions About Jobs

## How can I control the instantiation of Jobs?

See Quartz.Spi.IJobFactory and the Quartz.IScheduler.JobFactory property.

## How do I keep a Job from being removed after it completes?

Set the property JobDetail.Durable = true - which instructs Quartz not to
delete the Job when it becomes an "orphan" (when the Job not longer has a
Trigger referencing it).

## How do I keep a Job from firing concurrently?

**Quartz.NET 2.x**

Implement **IJob** and also decorate your job class with `[DisallowConcurrentExecution]` attribute. Read the API
documentation for `DisallowConcurrentExecutionAttribute` for more information.

**Quartz.NET 1.x**

Make the job class implement `IStatefulJob` rather than `IJob`. Read the API
documentation for `IStatefulJob` for more information.

## How do I stop a Job that is currently executing?

Quartz 1.x and 2x: See the `Quartz.IInterruptableJob` interface, and the `IScheduler.Interrupt(string, string)` method.

Quartz 3.x: See `IJobExecutionContext`'s `CancellationToken.IsCancellationRequested`

# Questions About Triggers

## How do I chain Job execution? Or, how do I create a workflow?

There currently is no "direct" or "free" way to chain triggers with Quartz.
However there are several ways you can accomplish it without much effort.
Below is an outline of a couple approaches:

One way is to use a listener (i.e. a TriggerListener, JobListener or
SchedulerListener) that can notice the completion of a job/trigger and then
immediately schedule a new trigger to fire. This approach can get a bit
involved, since you'll have to inform the listener which job follows which

* and you may need to worry about persistence of this information.

Another way is to build a Job that contains within its JobDataMap the name
of the next job to fire, and as the job completes (the last step in its
`Execute()` method) have the job schedule the next job. Several people are
doing this and have had good luck. Most have made a base (abstract) class
that is a Job that knows how to get the job name and group out of the
JobDataMap using special keys (constants) and contains code to schedule the
identified job. Then they simply make extensions of this class that included
the additional work the job should do.

In the future, Quartz will provide a much cleaner way to do this, but until
then, you'll have to use one of the above approaches, or think of yet another
that works better for you.

## Why isn't my trigger firing?

The most common reason for this is not having called `Scheduler.Start()`,
which tells the scheduler to start firing triggers.

The second most common reason is that the trigger or trigger group
has been paused.

## Daylight Saving Time and Triggers

CronTrigger and SimpleTrigger each handle daylight savings time in their own
way - each in the way that is intuitive to the trigger type.

First, as a review of what daylight savings time is, please read this resource:
<http://webexhibits.org/daylightsaving/g.html> . Some readers may be unaware
that the rules are different for different nations/contents. For example,
the 2005 daylight savings time starts in the United States on April 3, but
in Egypt on April 29. It is also important to know that not only the dates
are different for different locals, but the time of the shift is different
as well. Many places shift at 2:00 am, but others shift time at 1:00 am,
others at 3:00 am, and still others right at midnight.

SimpleTrigger allows you to schedule jobs to fire every N milliseconds.
As such, it has to do nothing in particular with respect to daylight
savings time in order to "stay on schedule" - it simply keeps firing every
N milliseconds. Regardless your SimpleTrigger is firing every 10 seconds,
or every 15 minutes, or every hour or every 24 hours it will continue to do
so. However the implication of this which confuses some users is that if
your SimpleTrigger is firing say every 12 hours, before daylight savings
switches it may be firing at what appears to be 3:00 am and 3:00 pm,
but after daylight savings 4:00 am and 4:00 pm. This is not a bug

* the trigger has kept firing exactly every N milliseconds, it just that the
"name" of that time that humans impose on that moment has changed.

CronTrigger allows you to schedule jobs to fire at certain moments with
respect to a "Gregorian calendar". Hence, if you create a trigger to fire
every day at 10:00 am, before and after daylight savings time switches it
will continue to do so. However, depending on whether it was the Spring or
Autumn daylight savings event, for that particular Sunday, the actual time
interval between the firing of the trigger on Sunday morning at 10:00 am
since its firing on Saturday morning at 10:00 am will not be 24 hours,
but will instead be 23 or 25 hours respectively.

There is one additional point users must understand about CronTrigger with
respect to daylight savings. This is that you should take careful thought
about creating schedules that fire between midnight and 3:00 am (the critical
window of time depends on your trigger's locale, as explained above).
The reason is that depending on your trigger's schedule, and the particular
daylight event, the trigger may be skipped or may appear to not fire for an
hour or two. As examples, say you are in the United States, where daylight
savings events occur at 2:00 am. If you have a CronTrigger that fires every
day at 2:15 am, then on the day of the beginning of daylight savings time
the trigger will be skipped, since, 2:15 am never occurs that day. If you
have a CronTrigger that fires every 15 minutes of every hour of every day,
then on the day daylight savings time ends you will have an hour of time
for which no triggers will occur, because when 2:00 am arrives, it will become
1:00 am again, however all of the firings during the one o'clock hour have
already occurred, and the trigger's next fire time was set to 2:00 am

* hence for the next hour no triggering will occur.

In summary, all of this makes perfect sense, and should be easy to remember
if you keep these two rules in mind:

* SimpleTrigger ALWAYS fires exactly every N seconds,  with no relation to the time of day.
* CronTrigger ALWAYS fires at a given time of day and then computes its  next time to fire. If that time does not occur on a given day, the  trigger will be skipped. If the time occurs twice in a given day, it only fires once, because after firing on that time the first time, it computes the next time of day to fire on.

# Questions About AdoJobStore

## How do I improve the performance of AdoJobStore?

There are a few known ways to speed up AdoJobStore, only one of which is
very practical.

First, the obvious, but not-so-practical:

* Buy a better (faster) network between the machine that runs Quartz, and the machine that runs your RDBMS.
* Buy a better (more powerful) machine to run your database on.
* Buy a better RDBMS.

Secondly, use driver delegate implementation that is specific to your database, like `SQLServerDelegate`, for best performance.

::: tip
You should also always prefer the latest version of the library. Quartz.NET 2.0 is much more efficient than 1.x series and 2.2.x line again has AdoJobStore related performance improvements over earlier 2.x releases.
:::

# Quartz in web environment

## Scheduler keeps stopping when application pool gets recycled

By default IIS recycles and stops app pools from time to time. This means that even if you have Application_Start event to start Quartz when web app is being first accessed, the scheduler might get disposed later on due to site inactivity.

If you have a IIS 8 available, you can configure your site to be pre-loaded and kept running. See [this blog post](https://blogs.msdn.microsoft.com/vijaysk/2012/10/11/iis-8-whats-new-website-settings/) for details.
