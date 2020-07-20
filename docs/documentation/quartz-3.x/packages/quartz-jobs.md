[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides some useful ready-mady jobs for your convenience.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

    Install-Package Quartz.Jobs

## Features

### DirectoryScanJob

Inspects a directory and compares whether any files' "last modified dates" have changed since the last time it was inspected.
If one or more files have been updated (or created), the job invokes a "call-back" method on an `IDirectoryScanListener`that can be found in the `SchedulerContext`.

### FileScanJob

Inspects a file and compares whether its "last modified dates" have changed since the last time it was inspected.
If one or more files have been updated (or created), the job invokes a "call-back" method on an `IFileScanListener`that can be found in the `SchedulerContext`.

### NativeJob

Built in job for executing native executables in a separate process.

**Example***
```csharp
var job = new JobDetail("dumbJob", null, typeof(Quartz.Jobs.NativeJob));
job.JobDataMap.Put(Quartz.Jobs.NativeJob.PropertyCommand, "echo \"hi\" >> foobar.txt");
var trigger = TriggerUtils.MakeSecondlyTrigger(5);
trigger.Name = "dumbTrigger";
await scheduler.ScheduleJob(job, trigger);
```

If PropertyWaitForProcess is true, then the integer exit value of the process will be saved as the job execution result in the `JobExecutionContext`.

### SendMailJob

A Job which sends an e-mail with the configured content to the configured recipient.
