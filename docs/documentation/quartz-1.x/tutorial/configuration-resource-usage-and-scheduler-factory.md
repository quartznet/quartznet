---

title: 'Lesson 10: Configuration, Resource Usage and SchedulerFactory'
---

Quartz is designed in modular way, and therefore to get it running, several components need to be "snapped" together.
Fortunately, some helpers exist for making this happen.

The major components that need to be configured before Quartz can do its work are:

* ThreadPool
* JobStore
* DataSources (if necessary)
* The Scheduler itself

The ThreadPool provides a set of Threads for Quartz to use when executing Jobs.
The more threads in the pool, the greater number of Jobs that can run concurrently.
However, too many threads may bog-down your system.
Most Quartz users find that 5 or so threads are plenty- because they have fewer than 100 jobs at any given time,
the jobs are not generally scheduled to run at the same time, and the jobs are short-lived (complete quickly).
Other users find that they need 10, 15, 50 or even 100 threads - because they have tens-of-thousands
of triggers with various schedules - which end up having an average of between 10 and 100 jobs trying to
execute at any given moment. Finding the right size for your scheduler's pool is completely dependent on
what you're using the scheduler for. There are no real rules, other than to keep the number of threads as
small as possible (for the sake of your machine's resources) - but make sure you have enough for your Jobs to fire on time.
Note that if a trigger's time to fire arrives, and there isn't an available thread,
Quartz will block (pause) until a thread comes available, then the Job will execute -
some number of milliseconds later than it should have. This may even cause the tread to misfire - if
there is no available thread for the duration of the scheduler's configured "misfire threshold".

A IThreadPool interface is defined in the Quartz.Spi namespace, and you can create a IThreadPool implementation in any way you like.
Quartz ships with a simple (but very satisfactory) thread pool named Quartz.Simpl.SimpleThreadPool.
This IThreadPool implementation simply maintains a fixed set of threads in its pool - never grows, never shrinks.
But it is otherwise quite robust and is very well tested - as nearly everyone using Quartz uses this pool.

JobStores and DataSources were discussed in Lesson 9 of this tutorial. Worth noting here, is the fact that all JobStores
implement the IJobStore interface - and that if one of the bundled JobStores does not fit your needs, then you can make your own.

Finally, you need to create your Scheduler instance. The Scheduler itself needs to be given a name and handed
instances of a JobStore and ThreadPool.

## StdSchedulerFactory

StdSchedulerFactory is an implementation of the ISchedulerFactory interface.
It uses a set of properties (NameValueCollection) to create and initialize a Quartz Scheduler.
The properties are generally stored in and loaded from a file, but can also be created by your program and handed directly to the factory.
Simply calling getScheduler() on the factory will produce the scheduler, initialize it (and its ThreadPool, JobStore and DataSources),
and return a handle to its public interface.

There are some sample configurations (including descriptions of the properties) in the "docs/config" directory of the Quartz distribution.
You can find complete documentation in the "Configuration" manual under the "Reference" section of the Quartz documentation.

## DirectSchedulerFactory

DirectSchedulerFactory is another SchedulerFactory implementation. It is useful to those wishing to create their Scheduler
instance in a more programmatic way. Its use is generally discouraged for the following reasons: (1) it
requires the user to have a greater understanding of what they're doing, and (2) it does not allow for declarative
configuration - or in other words, you end up hard-coding all of the scheduler's settings.

## Logging

Quartz.NET uses the [Common.Logging framework](http://netcommon.sourceforge.net/) for all of its logging needs.
Quartz does not produce much logging information - generally just some information during initialization, and
then only messages about serious problems while Jobs are executing. In order to "tune" the logging settings
(such as the amount of output, and where the output goes), you need to understand the `Commmon.Logging framework`,
which is beyond the scope of this document, please refer to [Common.Logging Documentation](http://netcommon.sourceforge.net/documentation.html).
