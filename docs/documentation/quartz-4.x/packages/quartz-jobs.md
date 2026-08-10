---

title : Jobs
---

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides some useful ready-made jobs for your convenience.

Quartz provides a number of utility jobs that you can use in your application for doing things like sending
e-mails and invoking native processes. These out-of-the-box jobs live in the `Quartz.Jobs` namespace, which is
also the assembly and NuGet package name. In 3.x the namespace was the singular `Quartz.Job`; a configuration
string or a stored `JOB_CLASS_NAME` naming the old spelling still resolves, with a warning.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
Install-Package Quartz.Jobs
```

## Features

### DirectoryScanJob

Inspects a directory and compares whether any files' "last modified dates" have changed since the last time it was inspected.
If one or more files have been updated (or created), the job invokes a "call-back" method on an `IDirectoryScanListener`that can be found in the `SchedulerContext`.

### FileScanJob

Inspects a file and compares whether its "last modified dates" have changed since the last time it was inspected.
If one or more files have been updated (or created), the job invokes a "call-back" method on an `IFileScanListener`that can be found in the `SchedulerContext`.

### NativeJob

Built in job for executing native executables in a separate process.

**Example**

```csharp
var job = JobBuilder.Create<NativeJob>()
    .WithIdentity("dumbJob")
    .UsingJobData(NativeJob.PropertyCommand, "echo \"hi\" >> foobar.txt")
    .Build();

var trigger = TriggerBuilder.Create()
    .WithIdentity("dumbTrigger")
    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);
```

If PropertyWaitForProcess is true, then the integer exit value of the process will be saved as the job execution result in the `JobExecutionContext`.

### SendMailJob

A Job which sends an e-mail with the configured content to the configured recipient.
