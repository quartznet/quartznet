# Quartz.Jobs

[Quartz.Jobs](https://www.nuget.org/packages/Quartz.Jobs) provides a set of ready-made jobs for common tasks.

## Installation

```shell
dotnet add package Quartz.Jobs
```

## Included jobs

* **DirectoryScanJob** — watches a directory and notifies an `IDirectoryScanListener` when files are added or modified since the last scan.
* **FileScanJob** — watches a single file and notifies an `IFileScanListener` when it changes.
* **NativeJob** — runs a native executable in a separate process.
* **SendMailJob** — sends an e-mail with configured content to a configured recipient.

## Documentation

📖 Full documentation: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/quartz-jobs.html>
