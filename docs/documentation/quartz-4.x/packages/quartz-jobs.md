---

title: Jobs
---

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides ready-made utility jobs, such as sending
e-mail and running native processes. The namespace, assembly and NuGet package are all `Quartz.Jobs`. In 3.x
the namespace was the singular `Quartz.Job`; a configuration string or stored `JOB_CLASS_NAME` with the old
spelling still resolves, with a warning.

## Installation

```shell
dotnet add package Quartz.Jobs
```

## How these jobs are configured

Each job reads its settings from its `JobDataMap`, under the keys listed with it below. The keys are the
persisted form: what a job store writes, a cluster shares, and an XML or JSON scheduling file names.

Each job also has an options type that maps to exactly those keys, and an extension that writes it. The stored
job is the same; the options type prevents misspelled keys and wrongly typed values.

| Job | Options | Extension |
|---|---|---|
| `DirectoryScanJob` | `DirectoryScanOptions` | `UsingDirectoryScanOptions(…)` |
| `FileScanJob` | `FileScanOptions` | `UsingFileScanOptions(…)` |
| `NativeJob` | `NativeJobOptions` | `UsingNativeJobOptions(…)` |
| `SendMailJob` | `SendMailOptions` | `UsingSendMailOptions(…)` |

The extensions work on `JobBuilder.Create<TJob>()` and on the configurator `AddJob<TJob>(…)` passes you, and
return the same builder so the chain continues. `Options.FromJobData(map)` reads the settings back from a job's
data.

## Features

### DirectoryScanJob

Scans directories for files whose last-modified time changed since the previous scan. When files were updated,
created or deleted, it calls an `IDirectoryScanListener`.

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

`MinimumUpdateAge` is how long a file must be unchanged before it is reported, so a file another process is
still writing is not handed over half-finished.

The listener is resolved in this order:

1. **A keyed registration**: `AddKeyedSingleton<IDirectoryScanListener>("inbox", …)`, and
   `ScanListenerName = "inbox"`.
2. **Dependency injection by type name**: register your implementation **as `IDirectoryScanListener`**
   (`AddSingleton<IDirectoryScanListener, InboxListener>()`) and set `ScanListenerName = nameof(InboxListener)`.
3. **`SchedulerContext`**: store the instance under a key, and name that key.

::: warning Registering the concrete type alone is no longer enough
Until 4.0 rc.1 the name was resolved by sweeping every loaded assembly with `GetTypes()`, so
`AddSingleton<InboxListener>()` was found, and so was a same-named type from any other assembly. Neither is
found now. Register the listener as `IDirectoryScanListener`, or key it.
:::

<!-- snippet: sample_jobs_scan_listener_context -->
```csharp
scheduler.Context["inboxListener"] = new InboxListener();
```
<!-- endSnippet -->

::: warning The scheduler context is not a secret store
`GET {ApiPath}/schedulers/{name}/context` returns **every** entry, falling back to `Convert.ToString`. For a
record or struct with a compiler-generated `ToString`, that is every field. Any authorized caller can read the
context, as with a job's data map. Store a shared *instance* or a name there; keep connection strings and API
keys in `IConfiguration`, a key vault or the container.
:::

To choose directories at run time, implement `IDirectoryProvider`, store the instance in the
`SchedulerContext`, and set `DirectoryProviderName` to that key. It receives the merged job data and returns the
paths to scan.

The job keeps the last modification time and file list it saw in the job detail's data map, so it is
`[PersistJobDataAfterExecution]`. The file list is a `Dictionary<string, string>` of full path to last-write
ticks under `CURRENT_FILE_LIST`, which both shipped serializers accept. Before 4.0 rc.1 it was a
`List<FileInfo>`, which neither can read back: the first firing against a persistent store failed to persist,
and the HTTP API refused to read the job's data map.

### FileScanJob

Checks one file's last-modified time. When it changed since the previous check, the job calls an
`IFileScanListener` found in the `SchedulerContext`.

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

- With `WaitForProcess` on, the process exit code is the job execution result in the `IJobExecutionContext`.
- Turn `ConsumeStreams` on for a process with a lot of output: one that fills its pipe blocks until the output
  is read.

::: danger Referencing this package changes what an open scheduling endpoint means
The [HTTP API](http-api.md) and the [dashboard](dashboard.md) schedule a job whose type is a **string from the
request**. It is resolved later with `Type.GetType` against the host's probing path. There is no allow-list;
only the shape of the name is validated. Once `Quartz.Jobs` is referenced, `NativeJob` is on that path, and it
runs the executable and arguments its job data names. An unauthenticated Quartz endpoint in such a process is
remote code execution.

**You may have this package without referencing it.** `Quartz.Plugins` depends on `Quartz.Jobs`, so an
application using the plugins (for XML scheduling, say) has `NativeJob` on its probing path. Check your restored
package graph, not your project file.

Neither surface starts when its mapping says nothing about authorization. `DirectoryScanJob` and `FileScanJob`
also take their paths from job data, and `SendMailJob` reads an SMTP credential from job data unless one is
registered; see [Keep the SMTP credential out of job data](#keep-the-smtp-credential-out-of-job-data).
:::

### SendMailJob

Sends an e-mail.

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
| `EnableSsl` | `smtp_enable_ssl` | `false` |

Override `Send(MailInfo, CancellationToken)` to send through something other than `SmtpClient`, or
`BuildMessage(SendMailOptions)` to add an attachment or header before sending.

::: warning This job is an authenticated relay for whoever can schedule it
`Sender`, `Recipient`, `Subject`, `Message` and `SmtpHost` are all caller data. Anyone who can schedule a job
can send mail from any address to any address through your server.
:::

`EnableSsl` is off by default, as in `SmtpClient`. Turning it on fails against a server that does not offer TLS,
such as an existing relay on the same host. Turn it on for anything that crosses a network you do not own, and
for anything that authenticates: SMTP `AUTH LOGIN` is base64, not encryption.

#### Keep the SMTP credential out of job data

`SendMailOptions` has no user name or password on purpose. A persistent job store writes job data to
`QRTZ_JOB_DETAILS`; every cluster node reads it, the dashboard shows it, and any export of the table carries it.

Register the credential with the container instead, **bound to its server**:

<!-- snippet: sample_jobs_smtp_credentials -->
```csharp
// Bound to the server it belongs to. The host to send through is job data, so a credential that
// answers for every host would go to whatever that data names.
CredentialCache credentials = new();
credentials.Add("smtp.example.com", 587, "Basic", new NetworkCredential("mailer", smtpPassword));

services.AddSingleton<ICredentialsByHost>(credentials);
```
<!-- endSnippet -->

Use `CredentialCache`, for security. `smtp_host` is job data, chosen by whoever scheduled the job. A bare
`NetworkCredential` answers `ICredentialsByHost.GetCredential` with itself for *every* host, which would send
the login, as base64 `AUTH LOGIN`, to any host the job data names.

| Registered | Result |
|---|---|
| `CredentialCache` with an entry for the job's host | That entry is used |
| `CredentialCache` with **no** entry for it | Mail is sent unauthenticated |
| Bare `NetworkCredential` | The job **refuses to send**, naming the host and how to bind the credential |
| Your own `ICredentialsByHost` | Asked `GetCredential(host, port, "Basic")`, then `"login"`; its answer is used |

Keep the password with your other secrets (user secrets in development, a key vault or environment variable in
production) and pass it in through `IConfiguration`.

When nothing is registered, the `smtp_username` and `smtp_password` job data keys are still read, so jobs from
earlier versions keep sending. The rule above does not apply to them, since the same data names the host. The
job logs a warning when it uses them, and a registered credential wins.

### NoOpJob

Does nothing. Use it as a placeholder, or to fire listeners on a schedule with no work attached.

## Registering these jobs with the container

The jobs take their dependencies (`TimeProvider`, `IServiceProvider`, `ICredentialsByHost`) from the container.
Register them like your own jobs:

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
