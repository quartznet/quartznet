---

title : Jobs
---

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides ready-made jobs.

## Installation

```shell
Install-Package Quartz.Jobs
```

## Features

### DirectoryScanJob

Checks whether any file's "last modified date" in a directory has changed since the last inspection. If files were updated (or created), it calls back an `IDirectoryScanListener` found in the `SchedulerContext`.

### FileScanJob

Checks whether a file's "last modified date" has changed since the last inspection. If it was updated (or created), it calls back an `IFileScanListener` found in the `SchedulerContext`.

### NativeJob

Runs a native executable in a separate process.

**Example**

```csharp
var job = new JobDetail("dumbJob", null, typeof(Quartz.Jobs.NativeJob));
job.JobDataMap.Put(Quartz.Jobs.NativeJob.PropertyCommand, "echo \"hi\" >> foobar.txt");
var trigger = TriggerUtils.MakeSecondlyTrigger(5);
trigger.Name = "dumbTrigger";
await scheduler.ScheduleJob(job, trigger);
```

If PropertyWaitForProcess is true, the process's integer exit value is saved as the job execution result in the `JobExecutionContext`.

### SendMailJob

Sends an e-mail with the configured content to the configured recipient.
