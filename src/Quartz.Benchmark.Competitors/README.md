# Quartz.Benchmark.Competitors

Quartz.NET against the other .NET schedulers it gets compared to — TickerQ 10.4.0 and Hangfire
1.8.25 — on what a scheduler actually does: firing jobs.

Nothing here ships. The project is **not in `Quartz.slnx`**, so `Compile`, `UnitTest`,
`BenchmarkSmoke`, `ExamplesSmoke`, the trim canary and Sonar never see it; it is `IsPackable=false`
with analysis off; and it references `Quartz.csproj` rather than a published package, so what it
measures is the working tree. Build and run it by naming it.

## Why it exists

The published third-party comparison this answers
(`benchmarks/TickerQ.Benchmarks/Comparisons/` in TickerQ) measures three different things and puts
them in one table. Its `ConcurrentThroughputComparison` baseline is a `FrozenDictionary<string,
TickerFunctionDelegate>` lookup followed by invoking a delegate whose body is `Task.CompletedTask`;
the Hangfire arms beside it create a background job in storage; the Quartz arms build an
`IJobDetail` and an `ITrigger` and call `ScheduleJob`. `JobCreationComparison`'s baseline is
"TickerQ: FrozenDictionary function lookup". The arms run under `Parallel.For` with
`.GetAwaiter().GetResult()`, the Quartz reference is `Quartz` `3.14.*` reached through
`new StdSchedulerFactory()`, and nothing clears the scheduler or the storage between iterations, so
both stores grow for the length of the run while the counter that names each job keeps climbing.

No job is executed on any side of that table.

This harness executes one. **Every arm counts inside the running job** — an `Interlocked` counter
incremented by the job body, an absolute target published before the work is scheduled, and a
`ManualResetEventSlim` waited on with a timeout that turns a hang into an exception. A benchmark
invocation is *waiting for N executions to happen*.

## Running

```shell
dotnet build -c Release src/Quartz.Benchmark.Competitors/Quartz.Benchmark.Competitors.csproj

# one scenario, measured
dotnet run -c Release --project src/Quartz.Benchmark.Competitors -- --filter '*S1*'

# everything that needs no database, executed once with nothing measured
dotnet run -c Release --project src/Quartz.Benchmark.Competitors -- --smoke

# S4, which is a plain runner rather than a benchmark
dotnet run -c Release --project src/Quartz.Benchmark.Competitors -- --recurring
```

Release is not optional: BenchmarkDotNet refuses a non-optimized assembly.

S2 and `--commits` need a PostgreSQL database, started outside the process and named by an
environment variable — BenchmarkDotNet runs a process per case, so a container owned by a benchmark
would be started and thrown away once per case:

```shell
docker run -d --name quartz-competitors-pg -p 55432:5432 \
  -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet \
  postgres:15.1 -c shared_preload_libraries=pg_stat_statements

$env:QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'

dotnet run -c Release --project src/Quartz.Benchmark.Competitors -- --filter '*S2*'
dotnet run -c Release --project src/Quartz.Benchmark.Competitors -- --commits
```

The `shared_preload_libraries` argument is only needed for the statement census; without it
`--commits` still counts commits and says the statement column was not collected. The three
libraries share the database and each lives in its own schema at its own default: Quartz in
`public` (its shipped DDL creates unqualified `qrtz_*` tables), TickerQ in `ticker`, Hangfire in
`hangfire`.

## The scenarios

| | What it measures | Size |
|---|---|---|
| S1 | In-memory throughput: ns and bytes per execution | 20,000 one-offs due at one instant |
| S2 | The same against PostgreSQL, plus commits per execution | 2,000 one-offs |
| S3 | Schedule-to-execute latency on an idle engine | 200 repetitions |
| S4 | Recurring accuracy: does "every second" fire every second | 100 schedules × 60 s |
| S5 | What one schedule costs to write | 50,000 into an empty store |

## The settings, and why they are what they are

| | Quartz | TickerQ | Hangfire |
|---|---|---|---|
| Worker limit | `MaxConcurrency` **10** (its default) | `MaxConcurrency` **10** (default `ProcessorCount` = 32 here) | `WorkerCount` **10** (default `ProcessorCount × 5` = 160 here) |
| Poll / batch | `MaxBatchSize` 1 and no fire-ahead window (defaults); or batch = pool with a 1 s window (tuned) | `MinPollingInterval` **100 ms** (default 1 s) | `SchedulePollingInterval` **50 ms** in memory, **100 ms** on PostgreSQL (default 15 s) |
| Everything else | shipped defaults | shipped defaults | shipped defaults |

Ten workers is the one number all three had to be told, and it is the most consequential setting in
the file: left alone, Hangfire would have run this workload with sixteen times Quartz's workers.

**Quartz gets two rows, not one.** `Defaults` is what `AddQuartz(q => q.UseInMemoryStore())` gives
you: `MaxBatchSize` 1 and a zero fire-ahead window, so one acquisition round per firing. `Tuned` is
what `FireThroughputBenchmark` uses and what the fire-throughput numbers in
`../Quartz.Benchmark/README.md` were taken at: the batch tracks the pool and the window is a second.
Neither setting batches anything on its own — the store ends a batch at the first acquired trigger's
own fire time plus the window, and the scheduler refuses a batch larger than the pool that would have
to run it. A reader comparing a table against their own deployment needs to know which of the two
they have.

**Due times are aligned to a whole second.** Hangfire records a scheduled job's due time as a
whole-second Unix timestamp — `ScheduledState.Handler.Apply` stores `JobHelper.ToTimestamp(EnqueueAt)`
as the score and `DelayedJobScheduler` compares it against `ToTimestamp(now)` — so a job due at a
fractional instant becomes eligible at the *start* of the second containing it, which is up to a
second early. Before the alignment, Hangfire ran 4,319 of a 20,000 batch before the measured window
opened. TickerQ buckets by whole second too: `GetEarliestTimeTickers` takes the earliest due ticker's
second and returns everything inside it. `Harness.EnsureNothingRanEarly` fails a run where an engine
gets through more than 1% of the batch before the window opens, rather than publishing the number it
would have produced.

**Two seconds of lead** (twenty on PostgreSQL) puts the work above TickerQ's immediate-dispatch
threshold, so all three arms go through their scheduler's own path. The short-circuit paths are
measured too, in rows of their own: Hangfire's `Enqueue` in S1 and TickerQ's null `ExecutionTime` in
S3.

**The engine is rebuilt every iteration** in S1, S2 and S5, because two of these stores fill up.
TickerQ keeps every completed ticker and LINQ-scans everything it holds on each poll; Hangfire's
finished jobs live until their expiry sweep. Quartz is the one that does not — completing a one-off
trigger deletes it and, because the job detail has no other trigger and is not durable, deletes that
too — but a second iteration against the other two's leftovers would be measuring the leftovers.

**`[MemoryDiagnoser]` is on every class, and `Allocated` is process-wide.** BenchmarkDotNet reads
`GC.GetTotalAllocatedBytes`, which counts every thread, so the column is what one execution costs the
whole engine — polling loop, workers and all — rather than what one thread of it cost. It is also
exact whatever else the machine is doing, which the `Mean` column on a working machine is not.

**The counter is the job's first instruction, so a drain ends when the last job *starts*.** That is
what makes the three comparable — no arm gets to count a queue write as an execution — and it means
each engine's bookkeeping for that last execution is outside the window. Over twenty thousand
executions that is a one-in-twenty-thousand effect and is ignored; over one census window it was not,
and `--commits` keeps its counters running for three seconds past the last start so that the final
completion write is inside the count.

**Scheduling is not measured, so where a library has no batch API it is done concurrently.** Hangfire
is the only one of the three without one, and two thousand sequential round trips to PostgreSQL took
about sixteen seconds — longer than the lead time the harness needs. Eight at a time, in the
iteration setup, outside every measured window.

**On PostgreSQL the schema is emptied between iterations.** An in-memory engine gets a fresh store
for free because a new engine builds a new one; a database does not, and Hangfire's succeeded jobs
live until their expiry sweep while TickerQ keeps every completed ticker. S2 also runs five
iterations rather than seven, because each one is two thousand round trips and the better part of a
minute.

## What one firing does

Read from each library's source at the version this harness pins.

**Quartz.NET.** The scheduler thread acquires a batch of triggers from the store, waits until the
first one's fire time, calls `TriggersFired` to mark them and read their job details, and hands each
to the thread pool inside a `JobRunShell`. The shell creates a dependency-injection scope, builds the
job instance through it, runs the middleware pipeline, notifies any registered listeners, executes
the job, then calls `TriggeredJobComplete`, which writes the trigger forward — or deletes it and its
orphaned job detail, for a one-off — and releases it. On `RAMJobStore` that is one monitor and a
sorted set; #3802's profile puts the steady-state repeating-trigger path at about 2.5 KB a firing, and
the extra here is the one-off's removal. On the ADO store it is about 9.7 statements and 1.24 commits.

**TickerQ.** A background service polls. It asks the persistence provider for the earliest due
tickers — on the in-memory provider a LINQ scan of every ticker it holds, filtered, ordered and
materialised twice, which is why completed tickers matter — marks the whole second's worth `Queued`,
sleeps until they are due (never less than `MinPollingInterval`), marks them `InProgress`, and queues
each onto its own task scheduler. Running one costs a `CreateAsyncScope`, a linked
`CancellationTokenSource`, a `TickerFunctionContext`, two `Stopwatch`es, a registration in a static
cancellation-token manager and a final status write through the provider — in memory as well as on a
database. The job instance itself is newed up by source-generated code rather than resolved from the
scope. A ticker due within one second never reaches that loop at all:
`TickerManager.AddTimeTickerAsync` compares `ExecutionTime` against `now.AddSeconds(1)` and, when it
is inside, acquires and dispatches on the calling thread.

**Hangfire.** A scheduled job sits in a sorted set until the `DelayedJobScheduler` — which polls at
`SchedulePollingInterval` — moves it to the `Enqueued` state and puts its id on a queue. A worker
fetches the id, reads the job's `InvocationData` back out of storage and deserialises the method and
its arguments, transitions the job to `Processing`, runs the filter pipeline, activates the type
through the `JobActivator` (nothing to activate for a static method), invokes it through reflection,
then transitions to `Succeeded`. Every state transition is a storage write with a state-history
entry. There is no separate scheduling thread: the poll and the workers are all `BackgroundProcess`es
on one server. A job created with `Enqueue` skips the poll entirely.

## Results

The numbers are in [`../Quartz.Benchmark/README.md`](../Quartz.Benchmark/README.md), in the dated
section for this harness, beside the rest of the repository's measurements. They are not repeated
here, so there is one place to keep current.

## What could not be verified

- **`pg_stat_statements` is off unless the container is started for it.** A plain `postgres:15.1`
  has no `shared_preload_libraries`, and `--commits` says "not collected" rather than estimating.
- **TickerQ on SQLite is not measured.** The issue asks for a SQLite row "if it works"; it is not in
  the harness, and the reason is in the deviations below.
- **Hangfire's recurring row measures deviation from the nearest whole second, not from a scheduled
  instant.** A recurring job is turned into an ordinary background job and the occurrence it came
  from is not passed to it, and under the default `MisfireHandlingMode.Relaxed` the occurrence is
  rewritten to the current instant before the job is created (`RecurringJobEntity.ScheduleNext`), so
  there is no scheduled instant left to be late for. Quartz's and TickerQ's rows use what the engine
  hands the job — `IJobExecutionContext.ScheduledFireTimeUtc` and
  `TickerFunctionContext.ScheduledFor`.
- **S3's Hangfire `Schedule` row is phase-locked to the poll.** The scenario leaves 20 ms of quiet
  between repetitions and Hangfire's poll is 50 ms, so the two settle into a stable phase and the p50
  comes out near 30 ms with almost no spread. Read that row as "bounded by
  `SchedulePollingInterval`", not as a precise figure.
- **Which lock a contended firing is blocked on is not established here.** #3802's D1 profile has
  that question for Quartz; this harness does not profile any of the three.

## Findings this harness surfaced

Recorded rather than acted on.

- **A non-durable Quartz job with many triggers is quadratic to drain.** The first S1 arrangement put
  one job detail behind all 20,000 triggers. Each completion removes the trigger, and
  `RAMJobStore.RemoveTriggerNoLock` does a linear `List<TriggerWrapper>.Remove` over that job's
  trigger list and then materialises the job's remaining trigger keys into an array to decide whether
  the job is orphaned. At 20,000 triggers on one job that read **83 KB per firing** and ran
  quadratically. The scenario now schedules independent units — one job detail per trigger, which is
  the shape the other two libraries have — and reads 3.3–3.5 KB. Worth a look on its own; it is a
  shape a fan-out schedule reaches.
- **TickerQ's generator does not compile a `void` ticker function.** `[TickerFunction]` on a method
  returning `void` produces an `async` lambda with no return in
  `TickerQInstanceFactory.g.cs`, which is CS1643. The counting job here returns `Task` for that
  reason.
- **TickerQ's worker pool backs off up to 50 ms when idle.** `TickerQTaskScheduler`'s worker loop
  does `await Task.Delay(Math.Min(consecutiveStealFailures * 2, 50))` once it has failed to find work
  more than three times running. On an idle engine every worker is inside that delay, so a work item
  dispatched by the immediate path waits for one to come out — which is what S3's TickerQ row is made
  of.
- **Quartz's fire-ahead window trades punctuality for batching, and S4 prices it.** A second of
  window lets a batch hold triggers due up to a second later, and they fire at the batch's earliest
  fire time — so the tuned profile's worst deviation on a one-second schedule is ~1,000 ms against the
  default profile's ~15–23 ms. Both are in the S4 table.

## Deviations from #3802's D2 specification

- **The SQLite row is not there.** It was to be taken "only if the spike found it works"; the spike
  was not run, because the PostgreSQL scenario plus its database census already took the measurement
  budget for this box and a SQLite row with two writers is a question about SQLite's write lock rather
  than about either scheduler. It is a follow-up, not a gap in the tables that are published.
- **Hangfire's per-schedule row calls `IBackgroundJobClient` and `RecurringJobManager` rather than the
  static `BackgroundJob.Schedule` / `RecurringJob.AddOrUpdate`.** Those facades are a
  `JobStorage.Current` lookup in front of exactly these calls, and their client is cached in a `Lazy`
  that binds the first storage it ever sees — which would have made every iteration after the first
  write into the previous iteration's store.
- **S3 reports P50 and P95 from BenchmarkDotNet and P50/P95/P99 from the in-job timestamps.**
  BenchmarkDotNet has no P99 column; the published percentiles are the in-job ones, which stop where
  the job starts rather than where the waiting thread wakes.
- **S4's Quartz rows use the default profile**, and the tuned profile is a third row rather than the
  setting the other two use. Measuring punctuality with a one-second fire-ahead window would have been
  measuring the window.
- **S2's lead time is twenty seconds rather than two, and Hangfire's scheduling is concurrent.**
  Hangfire has no batch API for creating jobs, so two thousand round trips took about sixteen seconds
  sequentially, which `Harness.EnsureScheduledBeforeDue` fails a run over rather than tolerating; eight
  at a time brings it well inside. Quartz's batch write of two thousand job details and two thousand
  triggers tripped the same guard once at ten seconds, which is why the lead is twenty. Scheduling is
  iteration setup and is outside every measured window.
- **S2 runs five iterations rather than seven**, because each one is two thousand round trips to a
  database and the better part of a minute.
- **The numbers were taken on `89fbadcdc2`, and #3801's cron fast path landed after it.** That work is
  on `CronExpression`'s parse and next-fire-time, so the S5 cron row and Quartz's S4 cron row are both
  due a re-run on top of it. Nothing else in these tables touches cron.
