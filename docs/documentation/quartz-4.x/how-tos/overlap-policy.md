---

title: Overlap Policy
---

# Overlap Policy

A trigger's overlap policy says what happens when one of its firings comes due while an earlier firing of
the same trigger is still running. From 4.3.

<!-- snippet: sample_overlap_policy_builder -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ReportJob>(j => j.WithIdentity("report"));

    q.AddTrigger(t => t
        .ForJob("report")
        .WithIdentity("every-five-minutes")
        .WithCronSchedule("0 0/5 * * * ?")
        // A report that is still running when the next one is due: drop the next one.
        .WithOverlapPolicy(OverlapPolicy.Skip));
});
```
<!-- endSnippet -->

## The policies

| `OverlapPolicy` | The due firing |
|---|---|
| `Default` | Starts beside the running one. What every trigger did before 4.3 |
| `Skip` | Is dropped; the trigger moves on to its next occurrence |
| `BufferOne` | Waits for the running one to end, then starts. At most one is kept |
| `CancelPrevious` | Starts; the running one is interrupted |
| `AllowAll` | Starts beside the running one, stated explicitly |

* Only the trigger's own firings count. Another trigger of the same job running is not an overlap.
* A retry is never skipped, held or cancelled: it continues an occurrence that already started.
* `[DisallowConcurrentExecution]` wins. While one of the job's firings runs, none of its triggers fires,
  whatever the policy says; a slot missed meanwhile is the misfire instruction's, as it always was.

## Skip

* The dropped occurrence is advanced past as a firing would, so it is **not a misfire**: the misfire
  instruction is not applied, `TriggerMisfired` is not raised, and the misfire count does not include it.
* `ITriggerListener.TriggerSkipped` is raised, with `NextFireTimeUtc` still the dropped occurrence.
* With [execution history](../tutorial/job-stores.md#execution-history-in-the-database) on, the skip is a
  misfire-history row with `Reason = MisfireReason.Overlap`:

<!-- snippet: sample_overlap_policy_history -->
```csharp
PagedResult<MisfireHistoryEntry> page = await history.QueryMisfires(
    new MisfireHistoryQuery { SchedulerName = schedulerName, Take = 50 });

foreach (MisfireHistoryEntry entry in page.Items)
{
    // Overlap: the trigger's Skip policy dropped the firing. Missed: a misfire.
    Console.WriteLine($"{entry.TriggerName} {entry.ScheduledFireTimeUtc:u} {entry.Reason}");
}
```
<!-- endSnippet -->

* A trigger whose skipped occurrence was its last is complete; the running firing's completion removes it.
* Log event [`2007`](../log-events.md) (in memory) or [`3043`](../log-events.md) (persistent store).

## BufferOne

While a firing runs, its trigger is `Blocked`, exactly as a `[DisallowConcurrentExecution]` job's triggers
are, but for this trigger alone. When the firing ends:

| The occurrence that came due was | Then |
|---|---|
| Within the misfire threshold | It fires now |
| Past the misfire threshold | The trigger's misfire instruction decides. A cron trigger's default fires once, now |

The misfire threshold is the store's `MisfireThreshold`: one minute for the persistent store, five seconds
in memory. Pausing and resuming a buffered trigger keeps it held until the firing ends.

## CancelPrevious

The running firing's cancellation token is signalled and the new firing starts. Cancellation is
cooperative, so the job has to watch its token:

<!-- snippet: sample_overlap_policy_cancellable_job -->
```csharp
public sealed class ReportJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        foreach (int page in Enumerable.Range(1, 40))
        {
            // CancelPrevious signals this token when the next firing starts; a job that ignores it
            // runs on beside the new one.
            cancellationToken.ThrowIfCancellationRequested();
            await RenderPage(page, cancellationToken);
        }
    }

    private static Task RenderPage(int page, CancellationToken cancellationToken) => Task.Delay(100, cancellationToken);
}
```
<!-- endSnippet -->

| The running firing is on | Then |
|---|---|
| This node | It is interrupted: `JobInterrupted` is raised, and log event [`1037`](../log-events.md) |
| Another node of a cluster | It cannot be interrupted from here, so the trigger waits for it, as `BufferOne`; log event [`3044`](../log-events.md) |

A job that ignores its token runs on beside the new firing.

## Change a policy

`TriggerDetailsUpdate.WithOverlapPolicy`, or the same trigger rebuilt with `WithOverlapPolicy`:

<!-- snippet: sample_overlap_policy_update -->
```csharp
// Decides from the next firing that comes due; the running one keeps its own.
await scheduler.UpdateTriggerDetails(
    new TriggerKey("every-five-minutes"),
    new TriggerDetailsUpdate().WithOverlapPolicy(OverlapPolicy.BufferOne));
```
<!-- endSnippet -->

* The new policy decides from the next firing that comes due. A firing already running keeps what it
  started with, and one held behind it under `BufferOne` still starts when it ends.
* A trigger given `CancelPrevious` while a firing of it that started under another policy runs on another
  node fires beside that firing rather than waiting for it, until no firing of it is running.

## Where it is set

| Where | How |
|---|---|
| Code | `TriggerBuilder.WithOverlapPolicy`, `ITriggerConfigurator.WithOverlapPolicy` |
| `quartz_jobs.xml` | `<overlap-policy>Skip</overlap-policy>`, after `continuation-condition` |
| `appsettings.json` `Quartz:Schedule`, `quartz_jobs.json` | `"OverlapPolicy": "Skip"` |
| HTTP API | `overlapPolicy` on the trigger body and on `PATCH …/triggers/{group}/{name}` |
| Dashboard | The trigger's page shows it |

Names are read in any case. Anything else is refused as the file is read.

## Persistent store

* The policy is `QRTZ_TRIGGERS.OVERLAP_POLICY`. Run
  [`4.3/add_overlap_policy_<db>.sql`](../../database/schema-changes.md#version-4-3) first; a 4.3 node
  refuses to start without it.
* The misfire-history reason is `QRTZ_MISFIRE_HISTORY.REASON`, added by the optional
  `4.3/add_misfire_reason_<db>.sql`, which only a store with `UseExecutionHistory()` needs.
* **Roll every node to 4.3 before giving a trigger a policy.** A 4.2 node ignores the column and fires the
  trigger as `Default` does, overlapping.
* Every decision is made under the `TRIGGER_ACCESS` lock, so `Skip` drops an occurrence once cluster-wide.
