---

title: Pausing with a Reason
---

# Pausing with a Reason

A pause can say why and who asked; the store adds when. Each pause member has a `*With` twin that takes a
`PauseDetails`. From 4.3.

<!-- snippet: sample_pause_with_reason -->
```csharp
await scheduler.PauseTriggerWith(
    new TriggerKey("nightly-export"),
    new PauseDetails { Reason = "vendor API is down until 18:00", RequestedBy = "alice" });
```
<!-- endSnippet -->

## The members

| Member | Pauses | Records the pause on |
|---|---|---|
| `PauseTriggerWith(key, details)` | one trigger | the trigger |
| `PauseJobWith(key, details)` | every trigger of the job | each trigger it paused |
| `PauseTriggerGroupsWith(matcher, details)` | the matching trigger groups | each group, and each trigger it paused |
| `PauseJobGroupsWith(matcher, details)` | the matching job groups | each group, and each trigger it paused |
| `PauseAllWith(details)` | every trigger group | each group, and each trigger it paused |

- Each answers what its reasonless twin answers and raises the same listener events.
- The key-set forms, `PauseTriggers` and `PauseJobs`, take no details.
- An already paused trigger or group **keeps the pause it had**.
- A reasonless pause is recorded too: `PausedAtUtc` set, `Reason` and `RequestedBy` `null`.

<!-- snippet: sample_pause_group_with_reason -->
```csharp
await scheduler.PauseJobGroupsWith(
    GroupMatcher<JobKey>.GroupEquals("billing"),
    new PauseDetails { Reason = "quarter close", RequestedBy = "finance-ops" });
```
<!-- endSnippet -->

| `PauseDetails` | Longest kept | Blank |
|---|---|---|
| `Reason` | 250 UTF-16 code units, `PauseDetails.MaxReasonLength`; longer is cut | `null` |
| `RequestedBy` | 200, `PauseDetails.MaxRequestedByLength`; longer is cut | `null` |

`PausedAtUtc` is the scheduler's clock, its `TimeProvider`.

## Reading it back

<!-- snippet: sample_pause_read -->
```csharp
PauseInfo? pause = await scheduler.GetTriggerPause(new TriggerKey("nightly-export"));
if (pause is not null)
{
    // Reason and RequestedBy are null when the pause did not say; PausedAtUtc is always set.
    Console.WriteLine($"Paused {pause.PausedAtUtc:u} by {pause.RequestedBy ?? "?"}: {pause.Reason}");
}
```
<!-- endSnippet -->

| Member | Answers |
|---|---|
| `GetTriggerPause(key)` | the trigger's own record; for a trigger born paused into a paused group, the trigger group's, then the job group's |
| `GetTriggerGroupPause(group)` | the trigger group's record |
| `GetJobGroupPause(group)` | the job group's record |
| `TriggerHeader.Pause` | the trigger's own record, on every [`QueryTriggers`](../tutorial/querying-jobs-and-triggers.md#headers-not-entities) row |

Each is `null` when the trigger or group is not paused, does not exist, or was paused by something that records
nothing (see [below](#a-mixed-cluster)). **Resuming clears the record.**

## Pausing when retries run out

`PauseTriggerWhenRetriesExhausted()` pauses a trigger whose [retry policy](retrying-failed-jobs.md) gives up,
instead of letting it return to its schedule and fail again.

<!-- snippet: sample_pause_when_retries_exhausted -->
```csharp
builder.Services.AddQuartz(q =>
{
    // A trigger whose retry policy gives up is paused, with the job's exception message as
    // the reason, until somebody resumes it.
    q.PauseTriggerWhenRetriesExhausted();

    q.AddJob<ExportJob>(j => j.WithIdentity("export"));
    q.AddTrigger(t => t
        .ForJob("export")
        .WithIdentity("nightly-export")
        .WithCronSchedule("0 0 2 * * ?")
        .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5))));
});
```
<!-- endSnippet -->

- `Reason` is the message of the exception the job threw, cut to 250.
- `RequestedBy` is `quartz:retries-exhausted`, so a listing tells it from an operator's pause.
- Only a trigger with a policy is ever paused; one with no next occurrence is finished instead.
- It is a trigger listener on [`TriggerRetriesExhausted`](retrying-failed-jobs.md#when-the-policy-gives-up).
  Calling it twice registers one.
- Resume the trigger once the cause is fixed. The dashboard's *Resume* does it.

## Over HTTP

The single-key, group and pause-all routes take an optional JSON body; no body is the reasonless pause. The
key-set `…/keys/pause` routes take none.

```http
POST /quartz-api/schedulers/core/triggers/reports/nightly-export/pause
Content-Type: application/json

{ "reason": "vendor API is down until 18:00", "requestedBy": "alice" }
```

- `requestedBy` left out is the authenticated user's name, `HttpContext.User.Identity.Name`.
- The state, group-paused and trigger-listing answers carry a `pause` object. Details:
  [HTTP API](../packages/http-api.md#a-pause-can-say-why).
- `AddQuartzHttpClient` sends and reads all of it, so `PauseTriggerWith` on a remote scheduler records the reason
  on the server.

## In the dashboard

- *Pause*, *Pause group* and *Pause all* ask for an optional reason.
- The requester is the signed-in user's name; `null` when anonymous.
- A paused trigger shows **Paused: reason (by who, when)** on its page and in the listings; a paused job group
  shows its record on the Jobs page.
- Read-only mode hides the prompts and keeps the notes.

## A mixed cluster

A persistent store keeps the record in three columns, `PAUSE_REASON`, `PAUSED_BY` and `PAUSED_AT`, on
`QRTZ_TRIGGERS` and both paused-group tables. [`4.3/add_pause_reason`](../../database/schema-changes.md#the-pause-columns)
adds them, and a 4.3 node refuses to start without them. A 4.2 node sharing the database neither writes nor
clears them.

| While the cluster runs both | What a 4.3 node reads |
|---|---|
| A 4.2 node pauses | `null`: no record |
| A 4.3 node pauses, a 4.2 node resumes | `null`: a record is read only while the trigger is paused or the group row exists |
| A 4.3 node pauses a trigger, a 4.2 node resumes it and pauses it again | **the first pause's record**, which is stale |

Roll every node to 4.3 before relying on the record.

## See also

- [Retrying Failed Jobs](retrying-failed-jobs.md) — the retry policy, and what happens when it gives up
- [Dashboard](../packages/dashboard.md#jobs-triggers-and-calendars) — pause, resume and the trigger pages
- [HTTP API](../packages/http-api.md#a-pause-can-say-why) — the pause body and the `pause` field
