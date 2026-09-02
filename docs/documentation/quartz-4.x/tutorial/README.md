---

title: Quartz.NET Tutorial
prev: false
next: false
---

<ApplicableVersion version="4.0" />

* [Lesson 1: Using Quartz](using-quartz.md)
* [Lesson 2: Jobs And Triggers](jobs-and-triggers.md)
* [Lesson 3: More About Jobs & JobDetails](more-about-jobs.md)
* [Lesson 4: Job Data](job-data-map.md)
* [Lesson 5: More About Triggers](more-about-triggers.md)
* [Lesson 6: Querying Jobs and Triggers](querying-jobs-and-triggers.md)
* [Lesson 7: SimpleTriggers](simpletriggers.md)
* [Lesson 8: CronTriggers](crontriggers.md)
* [Lesson 9: RecurrenceTrigger (RRULE)](recurrencetrigger.md)
* [Lesson 10: Time and TimeProvider](time-and-timeprovider.md)
* [Lesson 11: TriggerListeners & JobListeners](trigger-and-job-listeners.md)
* [Lesson 12: SchedulerListeners](scheduler-listeners.md)
* [Lesson 13: Job Execution Middleware](job-execution-middleware.md)
* [Lesson 14: JobStores](job-stores.md)
* [Lesson 15: Configuration, Resource Usage and Building a Scheduler](configuration-resource-usage-and-scheduler-factory.md)
* [Lesson 16: Building a Scheduler Without a Host](standalone-scheduler.md)
* [Lesson 17: Clustering](advanced-enterprise-features.md)
* [Lesson 18: Execution Groups](execution-groups.md)
* [Lesson 19: Node Affinity (Preferred Node)](node-affinity.md)
* [Lesson 20: Testing](testing.md)

The cron expression syntax the CronTriggers lesson builds on has its own page:
[Cron Expression Reference](../cron-expressions.md).

Four things a scheduler in production usually wants are recipes rather than lessons, and live in the
[How To's](../how-tos/):

* [Retrying Failed Jobs](../how-tos/retrying-failed-jobs.md) — a trigger re-fires its job after a failure
* [Rescheduling Jobs](../how-tos/rescheduling-jobs.md) — changing a schedule, and resetting a trigger stuck in `Error`
* [Running a Job Once](../how-tos/one-off-job.md) — work that happens at a time and is then finished with
* [Embedding Quartz in a Library](../how-tos/embedding-quartz-in-a-library.md) — for a package that schedules on its consumer's behalf

Several of these lessons have a runnable counterpart in the repository's
[console tour](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples/README.md) —
`dotnet run --project src/Quartz.Examples` — where simple triggers, cron triggers, job data, misfires,
listeners, calendars and clustering each happen in a console while you watch. Three fuller programs sit
beside it: [`Quartz.Examples.Worker`](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.Worker),
a worker service with a persistent store;
[`Quartz.Examples.AspNetCore`](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.AspNetCore),
which adds health checks, the HTTP API and the dashboard; and
[`Quartz.Examples.HttpClient`](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.HttpClient),
which drives that API from another process.
