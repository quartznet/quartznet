---

title: How To's
prev: false
next: false
---

# How To's

Each page answers one question. New to Quartz.NET? Start with the [Tutorial](../tutorial/).

* [One-Off Job](one-off-job.md) — fire a job once, now or at a given time
* [Rescheduling Jobs](rescheduling-jobs.md) — change a live schedule, retry a firing, recover a failed trigger
* [Retrying Failed Jobs](retrying-failed-jobs.md) — give a trigger a retry policy
* [Job Continuations](job-continuations.md) — run a trigger when another trigger's firing ends with an outcome you name
* [Progress and Execution Logs](progress-and-execution-logs.md) — show how far a running job has got
* [Multiple Triggers](multiple-triggers.md) — drive one job from several triggers, and give each its own data
* [Job Template](job-template.md) — the recommended skeleton for a job class
* [Running Quartz under Aspire](aspire.md) — telemetry, health and the database, wired to an AppHost
* [Quartz.NET with Wolverine](wolverine.md) — cron-publishing into a message bus, cancelling by correlation, and sharing the outbox's transaction
* [Coming from Hangfire](coming-from-hangfire.md) — the API mapping, and the semantics that differ
* [Coming from TickerQ](coming-from-tickerq.md) — the API mapping, and the semantics that differ
* [Embedding Quartz in a Library](embedding-quartz-in-a-library.md) — a package that runs inside an application it does not own
* [Running under an External Leader Election](external-leader.md) — one instance, started and stopped by another component's election
* [Publishing Trimmed and Native AOT](trimming-and-native-aot.md) — what each package claims, what still warns, and the fixes

Extending Quartz — the index, then the four seams the `Quartz.Impl.AdoJobStore` types exist for:

* [Extending Quartz](extending-quartz.md) — what is open, what is closed and why, and how to ask for a seam
* [A Job Store of Your Own](custom-job-store.md) — keeping scheduling data somewhere new, or decorating a store
* [A Driver Delegate for a New Database](dialect-delegate.md) — supporting a database Quartz does not ship a dialect for
* [Persisting a Custom Trigger Type](trigger-persistence-delegate.md) — storing a trigger family of your own without a blob
* [A Lock Handler of Your Own](lock-handler.md) — replacing the `QRTZ_LOCKS` row with something else

Reference material that these recipes lean on:

* [Cron Expression Reference](../cron-expressions.md) — the cron field and special-character syntax
* [Configuration Reference](../configuration/reference.md) — every option, typed and legacy
