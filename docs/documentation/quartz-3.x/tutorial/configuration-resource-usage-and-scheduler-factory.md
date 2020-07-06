---
title: 'Lesson 10: Configuration, Resource Usage and SchedulerFactory'
---

Quartz is architected in modular way, and therefore to get it running, several components need to be "snapped" together. 
Fortunately, some helpers exist for making this happen.

The major components that need to be configured before Quartz can do its work are:

* ThreadPool
* JobStore
* DataSources (if necessary)
* The Scheduler itself

Thread pooling has changed a lot since the Task-based jobs were introduced. **TODO document more** 

JobStores and DataSrouces were discussed in Lesson 9 of this tutorial. Worth noting here, is the fact that all JobStores 
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
instance in a more programatic way. Its use is generally discouraged for the following reasons: (1) it 
requires the user to have a greater understanding of what they're doing, and (2) it does not allow for declaritive 
configuration - or in other words, you end up hard-coding all of the scheduler's settings.

## Logging

Quartz.NET uses <a href="https://github.com/damianh/LibLog">LibLob library</a> for all of its logging needs. 
Quartz does not produce much logging information - generally just some information during initialization, and 
then only messages about serious problems while Jobs are executing. In order to "tune" the logging settings 
(such as the amount of output, and where the output goes), you need to actually configure your logging framework of choice as LibLog mostly delegates the work to
more full-fledged logging framework like log4net, serilog etc.

Please see <a href="https://github.com/damianh/LibLog/wiki">LibLog Wiki</a> for more information.
