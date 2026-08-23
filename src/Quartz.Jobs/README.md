# Quartz.Jobs

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides ready-made jobs for the things
schedules are most often asked to do.

| Job | What it does |
|---|---|
| `DirectoryScanJob` | watches a directory and calls an `IDirectoryScanListener` when files are added, changed or deleted |
| `FileScanJob` | watches a single file and calls an `IFileScanListener` when it changes |
| `NativeJob` | runs a native executable in a separate process |
| `SendMailJob` | sends an e-mail with configured content to a configured recipient |

## Installation

```shell
dotnet add package Quartz.Jobs
```

## Usage

Each job reads its settings from its `JobDataMap`. Those keys are the persisted form, so each job also
has an options type that writes exactly them — the key cannot be misspelled and the value cannot be of
the wrong type:

<!-- snippet: sample_readme_jobs -->
```csharp
builder.Services.AddQuartz(q => q.ScheduleJob<SendMailJob>(
    trigger => trigger.WithIdentity("nightlyDigest").WithCronSchedule("0 0 6 * * ?"),
    job => job.UsingSendMailOptions(new SendMailOptions
    {
        SmtpHost = "smtp.example.com",
        Sender = "scheduler@example.com",
        Recipient = "ops@example.com",
        Subject = "Nightly digest",
        Message = "Everything ran.",
    })));
```
<!-- endSnippet -->

`UsingDirectoryScanOptions`, `UsingFileScanOptions` and `UsingNativeJobOptions` are the same thing for
the other three, and all of them work on `JobBuilder.Create<TJob>()` as well as on the configurator
`AddJob<TJob>(…)` hands you.

These jobs live in the `Quartz.Jobs` namespace. In Quartz 3 it was the singular `Quartz.Job`; a
configuration string or a stored `JOB_CLASS_NAME` naming the old spelling still resolves, with a
warning.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/quartz-jobs.html>
