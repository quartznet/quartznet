---

title: How To's
prev: false
next: false
---

# How To's

Short, task-shaped recipes. Each page answers one question; the
[Tutorial](../tutorial/) is the place to start if you are new to Quartz.NET.

* [One-Off Job](one-off-job.md) — fire a job once, now or at a given time
* [Rescheduling Jobs](rescheduling-jobs.md) — change a live schedule, retry a firing, recover a failed trigger
* [Multiple Triggers](multiple-triggers.md) — drive one job from several triggers, and give each its own data
* [Job Template](job-template.md) — the recommended skeleton for a job class

Extending Quartz — the four seams the `Quartz.Impl.AdoJobStore` types exist for:

* [A Job Store of Your Own](custom-job-store.md) — keeping scheduling data somewhere new, or decorating a store
* [A Driver Delegate for a New Database](dialect-delegate.md) — supporting a database Quartz does not ship a dialect for
* [Persisting a Custom Trigger Type](trigger-persistence-delegate.md) — storing a trigger family of your own without a blob
* [A Lock Handler of Your Own](lock-handler.md) — replacing the `QRTZ_LOCKS` row with something else

Reference material that these recipes lean on:

* [Cron Expression Reference](../cron-expressions.md) — the cron field and special-character syntax
* [Configuration Reference](../configuration/reference.md) — every option, typed and legacy
