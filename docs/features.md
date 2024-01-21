---

title: Quartz.NET Features
---

## Runtime Environments

* Quartz.NET can run embedded within another free-standing application
* Quartz.NET can run as a stand-alone program (within its own .NET virtual machine instance), to be used via .NET Remoting
* Quartz.NET can be instantiated as a cluster of stand-alone programs (with load-balance and fail-over capabilities)

## Job Scheduling

Jobs are scheduled to run when a given Trigger occurs. Triggers can be created with nearly any combination of the following directives:

* at a certain time of day (to the millisecond)
* on certain days of the week
* on certain days of the month
* on certain days of the year
* not on certain days listed within a registered Calendar (such as business holidays)
* repeated a specific number of times
* repeated until a specific time/date
* repeated indefinitely
* repeated with a delay interval

Jobs are given names by their creator and can also be organized into named groups.
Triggers may also be given names and placed into groups, in order to easily organize them within the scheduler.
Jobs can be added to the scheduler once, but registered with multiple Triggers.

## Job Execution

* Jobs can be any .NET class that implements the simple IJob interface, leaving infinite possibilities for the work Jobs can perform.
* Job class instances can be instantiated by Quartz.NET, or by your application's framework.
* When a Trigger occurs, the scheduler notifies zero or more .NET objects implementing the JobListener and TriggerListener interfaces. These listeners are also notified after the Job has been executed.
* As Jobs are completed, they return a JobCompletionCode which informs the scheduler of success or failure. The JobCompletionCode can also instruct the scheduler of any actions it should take based on the success/fail code - such as immediate re-execution of the Job.

## Job Persistence

* The design of Quartz.NET includes an IJobStore interface that can be implemented to provide various mechanisms for the storage of jobs.
* With the use of the included AdoJobStore, all Jobs and Triggers configured as "non-volatile" are stored in a relational database via ADO.NET.
* With the use of the included RAMJobStore, all Jobs and Triggers are stored in RAM and therefore do not persist between program executions - but this has the advantage of not requiring an external database.

## Clustering

* Fail-over.
* Load balancing.

## Listeners & Plug-Ins

* Applications can catch scheduling events to monitor or control job/trigger behavior by implementing one or more listener interfaces.
* The Plug-In mechanism can be used to add functionality to Quartz, such as keeping a history of job executions, or loading job and trigger definitions from a file.
* Quartz ships with a number of "factory-built" plug-ins and listeners.
