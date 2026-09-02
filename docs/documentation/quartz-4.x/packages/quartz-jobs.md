---

title: Jobs
---

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides some useful ready-made jobs for your convenience.

Quartz provides a number of utility jobs that you can use in your application for doing things like sending
e-mails and invoking native processes. These out-of-the-box jobs live in the `Quartz.Jobs` namespace, which is
also the assembly and NuGet package name. In 3.x the namespace was the singular `Quartz.Job`; a configuration
string or a stored `JOB_CLASS_NAME` naming the old spelling still resolves, with a warning.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
dotnet add package Quartz.Jobs
```

## How these jobs are configured

Each of these jobs reads its settings from its `JobDataMap`, under the keys listed with it below. Those keys
are the persisted form: they are what a job store writes, what a cluster shares, and what an XML or JSON
scheduling file names.

Each job also has an options type that maps onto exactly those keys, and an extension that writes it. It is
the same stored job either way — but the key cannot be misspelled, the value cannot be of the wrong type, and
every setting the job honours is a named property you can find by typing a dot.

| Job | Options | Extension |
|---|---|---|
| `DirectoryScanJob` | `DirectoryScanOptions` | `UsingDirectoryScanOptions(…)` |
| `FileScanJob` | `FileScanOptions` | `UsingFileScanOptions(…)` |
| `NativeJob` | `NativeJobOptions` | `UsingNativeJobOptions(…)` |
| `SendMailJob` | `SendMailOptions` | `UsingSendMailOptions(…)` |

The extensions work on both configuration surfaces — `JobBuilder.Create<TJob>()` and the configurator
`AddJob<TJob>(…)` hands you — and each leaves you with what you started with, so the chain continues as usual.
`Options.FromJobData(map)` reads the same settings back out of a job's data.

## Features

### DirectoryScanJob

Inspects a directory and compares whether any files' "last modified dates" have changed since the last time it
was inspected. If one or more files have been updated, created or deleted, the job invokes a call-back method
on an `IDirectoryScanListener`.

<!-- snippet: sample_jobs_directory_scan -->
```csharp
IJobDetail job = JobBuilder.Create<DirectoryScanJob>()
    .WithIdentity("inboxScan")
    .UsingDirectoryScanOptions(new DirectoryScanOptions
    {
        Directories = ["/var/spool/inbox"],
        ScanListenerName = nameof(InboxListener),
        SearchPattern = "*.csv",
        IncludeSubDirectories = true,
        MinimumUpdateAge = TimeSpan.FromSeconds(30),
    })
    .Build();
```
<!-- endSnippet -->

| Setting | Job data key | Default |
|---|---|---|
| `Directories` | `DIRECTORY_NAMES` (semicolon-separated), or `DIRECTORY_NAME` for one | — |
| `DirectoryProviderName` | `DIRECTORY_PROVIDER_NAME` | none; the paths above are used |
| `ScanListenerName` | `DIRECTORY_SCAN_LISTENER_NAME` | required |
| `SearchPattern` | `SEARCH_PATTERN` | `*` |
| `IncludeSubDirectories` | `INCLUDE_SUB_DIRECTORIES` | `false` |
| `MinimumUpdateAge` | `MINIMUM_UPDATE_AGE`, in milliseconds | 5 seconds |

`MinimumUpdateAge` is how long a file must have been left alone before the job reports it. Without it a file
another process is still writing would be handed to the listener half-finished.

The listener is found in one of two ways, in this order:

1. **Dependency injection** (recommended): register your `IDirectoryScanListener` implementation in the
   container, and name its type — `ScanListenerName = nameof(InboxListener)`.
2. **`SchedulerContext`**: store the instance under a key, and name that key.

<!-- snippet: sample_jobs_scan_listener_context -->
```csharp
scheduler.Context["inboxListener"] = new InboxListener();
```
<!-- endSnippet -->

Where the directories come from can be decided at run time instead of being listed: implement
`IDirectoryProvider`, put the instance in the `SchedulerContext`, and name that key as
`DirectoryProviderName`. It is handed the merged job data and returns the paths to scan.

The job keeps its own bookkeeping — the last modification time it saw and the file list it saw it in — in the
job detail's data map, which is why it is `[PersistJobDataAfterExecution]`.

### FileScanJob

Inspects a single file and compares whether its "last modified date" has changed since the last time it was
inspected. If it has, the job invokes a call-back method on an `IFileScanListener` found in the
`SchedulerContext`.

<!-- snippet: sample_jobs_file_scan -->
```csharp
IJobDetail job = JobBuilder.Create<FileScanJob>()
    .WithIdentity("configWatch")
    .UsingFileScanOptions(new FileScanOptions
    {
        FileName = "/etc/app/settings.json",
        ScanListenerName = "settingsListener",
        MinimumUpdateAge = TimeSpan.FromSeconds(5),
    })
    .Build();
```
<!-- endSnippet -->

| Setting | Job data key | Default |
|---|---|---|
| `FileName` | `FILE_NAME` | required |
| `ScanListenerName` | `FILE_SCAN_LISTENER_NAME` | required |
| `MinimumUpdateAge` | `MINIMUM_UPDATE_AGE`, in milliseconds | 5 seconds |

### NativeJob

Runs a native executable in a separate process.

<!-- snippet: sample_jobs_native -->
```csharp
IJobDetail job = JobBuilder.Create<NativeJob>()
    .WithIdentity("dumbJob")
    .UsingNativeJobOptions(new NativeJobOptions
    {
        Command = "echo",
        Parameters = "\"hi\" >> foobar.txt",
    })
    .Build();

ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("dumbTrigger")
    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
    .Build();

await scheduler.ScheduleJob(job, trigger);
```
<!-- endSnippet -->

| Setting | Job data key | Default |
|---|---|---|
| `Command` | `command` | required |
| `Parameters` | `parameters` | none |
| `WaitForProcess` | `waitForProcess` | `true` |
| `ConsumeStreams` | `consumeStreams` | `false` |
| `WorkingDirectory` | `workingDirectory` | the scheduler's |

When `WaitForProcess` is on, the integer exit code of the process is saved as the job execution result in the
`IJobExecutionContext`. Turn `ConsumeStreams` on for a chatty process: one that writes more output than its
pipe holds blocks until someone reads it.

::: danger Referencing this package changes what an open scheduling endpoint means
Both HTTP surfaces — the [HTTP API](http-api.md) and the [dashboard](dashboard.md) — schedule a job whose
type is a **string the request supplies**. The name is stored unresolved and resolved later with
`Type.GetType` against whatever is on the host's probing path; there is no allow-list, and the only
validation is on the shape of the name. `NativeJob` is on that path as soon as `Quartz.Jobs` is
referenced, and it starts the executable its job data names with the arguments its job data names. So an
unauthenticated Quartz endpoint in a process that references this package is remote code execution rather
than an information leak.

Neither surface will start when its mapping says nothing about authorization, which
is what closes the common way into this. `DirectoryScanJob` and `FileScanJob` read the paths they scan
from job data the same way, and `SendMailJob` reads an SMTP credential from job data unless one is
registered — see [Keep the SMTP credential out of job data](#keep-the-smtp-credential-out-of-job-data).
:::

### SendMailJob

Sends an e-mail with the configured content to the configured recipient.

<!-- snippet: sample_jobs_send_mail -->
```csharp
IJobDetail job = JobBuilder.Create<SendMailJob>()
    .WithIdentity("nightlyDigest")
    .UsingSendMailOptions(new SendMailOptions
    {
        SmtpHost = "smtp.example.com",
        SmtpPort = 587,
        Sender = "scheduler@example.com",
        Recipient = "ops@example.com",
        Subject = "Nightly digest",
        Message = "Everything ran.",
    })
    .Build();
```
<!-- endSnippet -->

| Setting | Job data key | Default |
|---|---|---|
| `SmtpHost` | `smtp_host` | required |
| `SmtpPort` | `smtp_port` | the client's default |
| `Sender` | `sender` | required |
| `Recipient` | `recipient` | required |
| `CcRecipient` | `cc_recipient` | none |
| `ReplyTo` | `reply_to` | the sender |
| `Subject` | `subject` | required |
| `Message` | `message` | required |
| `Encoding` | `encoding` | the default |

Override `Send(MailInfo, CancellationToken)` to route the mail through something other than `SmtpClient`, or
`BuildMessage(SendMailOptions)` to add to the message — an attachment, a header — before it goes.

#### Keep the SMTP credential out of job data

`SendMailOptions` has no user name or password on purpose. Job data is durable: a persistent job store writes
it to `QRTZ_JOB_DETAILS`, every node in the cluster reads it, the dashboard shows it, and any export of that
table carries it. A password put there is a password in all of those places.

Register the credential with the container instead, and the job authenticates with it:

<!-- snippet: sample_jobs_smtp_credentials -->
```csharp
services.AddSingleton<ICredentialsByHost>(new NetworkCredential("mailer", smtpPassword));
```
<!-- endSnippet -->

`ICredentialsByHost` is what `SmtpClient.Credentials` takes, so a `CredentialCache` covers several servers.
The password itself belongs wherever the rest of your secrets live — user secrets in development, a key vault
or an environment variable in production — and reaches this registration through `IConfiguration`.

The `smtp_username` and `smtp_password` job data keys are still read when nothing is registered, so a job
scheduled by an earlier version keeps sending. The job logs a warning when it uses them, and a credential from
the container wins.

### NoOpJob

A job that does nothing. Useful as a placeholder, and for triggering listeners on a schedule without any work
attached.

## Registering these jobs with the container

The jobs take their dependencies — a `TimeProvider`, an `IServiceProvider`, an `ICredentialsByHost` — from the
container, so register them the same way you register your own:

<!-- snippet: sample_jobs_native_under_di -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<NativeJob>(j => j
        .WithIdentity("nightlyReport")
        .StoreDurably()
        .UsingNativeJobOptions(new NativeJobOptions
        {
            Command = "report.exe",
            Parameters = "--nightly",
            ConsumeStreams = true,
        }));

    q.AddTrigger<NativeJob>(t => t
        .ForJob("nightlyReport")
        .WithCronSchedule("0 0 2 * * ?"));
});
```
<!-- endSnippet -->
