# Quartz.Benchmark

The BenchmarkDotNet suites for Quartz.NET. Nothing here ships — the project sets `IsPackable` to
`false`, and it exists so that a change to a hot path can be argued with numbers instead of opinion.

## Running

One suite, measured properly:

```shell
dotnet run -c Release --project src/Quartz.Benchmark -- --filter "*CronExpressionBenchmark*"
```

Everything, executed once with nothing measured — the liveness check that the `BenchmarkSmoke` build
target runs on every pull request, and what to run by hand after touching a benchmark:

```shell
dotnet run -c Release --project src/Quartz.Benchmark -- --smoke
```

`--smoke` is a whole run rather than a modifier, so it takes no other arguments. It covers every
benchmark in the assembly except the two categories in `BenchmarkCategories`: `RequiresDatabase`,
which needs a database pointed at by an environment variable, and `LongRunning`, whose single dry
iteration is minutes. A benchmark written tomorrow is in the smoke run without anybody adding it to a
list, which is the property that makes the target worth having; see `Program.cs` for the rest.

Release is not optional. BenchmarkDotNet refuses a non-optimized assembly, and a smoke run of one
would prove nothing.

## Profiling

A benchmark says what something costs; a profile says where the cost is. BenchmarkDotNet is the wrong
thing to attach a sampling profiler to — a process per case, a pilot deciding how many invocations an
iteration gets, and its own machinery in every stack — so the workloads worth profiling are also
reachable as plain runs of this assembly, with the harness out of the way. `--help` lists them:

| Switch | What it runs |
|---|---|
| `--profile-fire` | `FireThroughputBenchmark`'s workload at `MaxConcurrency` 10 on `RAMJobStore`, for ~25 s |
| `--profile-cron` | `CronExpressionComparisonBenchmark.Next100`, for ~20 s |
| `--profile-schedule` | `ScheduleJobBenchmark`'s simple arm, clearing the store every 50,000, for ~20 s |
| `--latency` | The schedule-to-execute probe: one job scheduled for now on an idle scheduler, 200 times |

Each is a whole run and takes no other arguments, as `--smoke` does. Each prints what it got through
when it ends, so a capture can be checked against the rate the benchmark reports rather than assumed
to have measured the same thing. Nothing in CI runs any of them.

Build Release and profile the built exe rather than `dotnet run`, which would put MSBuild in the
trace. This project sets `UseArtifactsOutput` to `false`, so the exe is where it has always been:

```shell
dotnet build -c Release src/Quartz.Benchmark/Quartz.Benchmark.csproj

# ultra (xoofx/ultra), from an elevated shell — ETW needs it
ultra profile -o fire --delay 3 -- src/Quartz.Benchmark/bin/Release/net10.0/Quartz.Benchmark.exe --profile-fire
```

`--delay 3` skips the startup and the first JIT, which for `--profile-fire` also skips the scheduler
being built and the two thousand triggers being scheduled. The result is a Firefox Profiler capture,
readable at <https://profiler.firefox.com> or through the UltraMcp tools.

**Without an elevated shell ETW is not available at all**, and EventPipe is the fallback. It takes two
captures rather than one, because the two providers answer different questions and each distorts the
other:

```shell
# where the time goes
dotnet-trace collect --providers "Microsoft-DotNETCore-SampleProfiler:::SampleProfilerIntervalInMs=1" \
  -o fire-cpu.nettrace -- src/Quartz.Benchmark/bin/Release/net10.0/Quartz.Benchmark.exe --profile-fire
dotnet-trace convert fire-cpu.nettrace --format speedscope

# what it allocates, by type
dotnet-trace collect --profile gc-verbose \
  -o fire-gc.nettrace -- src/Quartz.Benchmark/bin/Release/net10.0/Quartz.Benchmark.exe --profile-fire
```

The speedscope conversion keeps sample events and nothing else, so it is for the first capture only;
read the second as `.nettrace`, in PerfView's GC Heap Alloc view or through
`Microsoft.Diagnostics.Tracing.TraceEvent`. `GCAllocationTick` fires once per ~100 KB and names the
type that crossed the threshold, so a type's share of the ticks estimates its share of the bytes, and
`[MemoryDiagnoser]`'s total is what turns that share into bytes per operation.

Two corrections a capture needs before it is ranked, whichever profiler took it:

- **The allocation-tick tax.** `ultra` enables the CLR GC keyword at Verbose, so every
  `GCAllocationTick` fires an ETW event with a stack walk. At the fire path's rate that is a large
  share of the trace and it distorts the category split as well as the function ranking: subtract the
  `adjust_limit_clr` → `fire_etw_allocation_event` subtree first, then read `module_breakdown`.
- **Parked threads and GC polls, on an EventPipe capture.** The sampler ticks every managed thread
  whether it is running or waiting, so a raw sum makes an idle worker the hottest code in the process:
  drop the intervals whose innermost real frame is a park primitive. It also catches a thread it
  cannot walk past at `Thread.<PollGC>g__PollGCWorker`, which was 11 % of a `--profile-fire` capture
  and 43 % of a `--profile-cron` one; drop those and renormalise, as #3802's report does.

The measurements these switches were written for are on
[#3802](https://github.com/quartznet/quartznet/issues/3802), which is where the profile per surface,
the top frames and the per-firing allocation attribution live. Numbers belong there and in the pull
requests that act on them rather than here, until a change lands that moves one of the tables below.

## Cron and RAMJobStore reference numbers (2026-08-30, AMD Ryzen 9 5950X)

Taken on `a620fc632` to answer #3538. TickerQ publishes a comparison putting
`CronExpression.GetNextValidTimeAfter` at ~1.2 µs and ~3 KB a call against NCrontab's 13 ns and
nothing, and `ScheduleJob` into `RAMJobStore` at 4.4 µs (simple trigger) and 31 µs / 38.7 KB (cron
trigger). Those runs pinned **Quartz 3.14**, which predates the bitmask cron fields (#3126-#3129) and
4.0's rebuilt `CronExpression`, so they had never been checked against this branch.

`CronExpressionComparisonBenchmark`, `JobAndTriggerBuilderBenchmark` and `ScheduleJobBenchmark` are
the reproduction. They use the published comparison's own expressions, its fixed search instant and
its operations, and they run NCrontab in the same process, so the reference row and the Quartz row
come off one table on one machine rather than off two tables on two.

**Machine and runtime.** BenchmarkDotNet v0.15.8; Windows 11 (10.0.26200.9168/25H2); AMD Ryzen 9
5950X 3.40 GHz, 1 CPU, 32 logical and 16 physical cores; .NET SDK 10.0.400; host and job
.NET 10.0.11 (10.0.1126.37416), X64 RyuJIT x86-64-v3. The machine's local time zone is
`FLE Standard Time` (UTC+02:00 Helsinki), which observes daylight saving — neither library is given a
time zone in these cases, exactly as the published comparison leaves them, so `CronExpression`
resolves against that zone and the interval expressions do reach the fall-back check.

**Read the Error column.** The machine had other work on it throughout, so the means carry more
spread than a quiet box would give. It does not touch the conclusions: the allocation column is exact
whatever the load, and the effects below are multiples rather than percentages.

### Cron parsing and next-fire-time

`CronExpressionComparisonBenchmark`, default job capped at 20 iterations
(`--maxIterationCount 20`; uncapped, BenchmarkDotNet's noise-driven extension made the suite a
~35-minute run on this machine).

| Method                     | Mean         | Error      | Allocated |
|--------------------------- |-------------:|-----------:|----------:|
| Parse_Simple               |    307.17 ns |  19.514 ns |     576 B |
| Parse_Simple_NCrontab      |    514.65 ns |  45.301 ns |    1816 B |
| Parse_Complex              |    337.66 ns |  32.430 ns |     656 B |
| Parse_Complex_NCrontab     |    391.74 ns |  12.117 ns |    1880 B |
| Parse_SecondLevel          |    236.21 ns |  15.722 ns |     688 B |
| Parse_SecondLevel_NCrontab |    404.46 ns |  27.500 ns |    2152 B |
| Next_Simple                |    362.27 ns |   4.275 ns |         - |
| Next_Simple_NCrontab       |     25.23 ns |   0.394 ns |         - |
| Next_Complex               |    372.61 ns |   5.579 ns |         - |
| Next_Complex_NCrontab      |     23.28 ns |   2.020 ns |         - |
| Next_SecondLevel           |    314.53 ns |  18.761 ns |         - |
| Next_SecondLevel_NCrontab  |     54.94 ns |  11.393 ns |         - |
| Next100                    | 38,434.24 ns | 748.355 ns |         - |
| Next100_NCrontab           |  2,799.70 ns |  26.359 ns |         - |

Against the published 3.14 table, per operation:

| Operation              | Quartz 3.14 (published) | Quartz 4.0 (here) | NCrontab (here) |
|----------------------- |------------------------:|------------------:|----------------:|
| Parse simple           |     3,835 ns / 10.8 KB   |   307 ns / 576 B  |  515 ns / 1816 B |
| Parse complex          |     3,017 ns / 8.7 KB    |   338 ns / 656 B  |  392 ns / 1880 B |
| Parse second-level     |     4,598 ns / 12.9 KB   |   236 ns / 688 B  |  404 ns / 2152 B |
| Next occurrence simple |     1,292 ns / 3.2 KB    |   362 ns / **0 B**|   25 ns / 0 B    |
| Next occurrence complex|     1,119 ns / 3.1 KB    |   373 ns / **0 B**|   23 ns / 0 B    |
| Next occurrence second |     1,318 ns / 2.7 KB    |   315 ns / **0 B**|   55 ns / 0 B    |
| 100 next occurrences   |   128,580 ns / 314 KB    | 38,434 ns / **0 B** | 2,800 ns / 0 B |

### Building a job detail and a trigger

`JobAndTriggerBuilderBenchmark`, same job.

| Method             | Mean        | Error      | Allocated |
|------------------- |------------:|-----------:|----------:|
| BuildJobDetail     |    95.85 ns |   5.708 ns |     672 B |
| BuildSimpleTrigger |    97.52 ns |   4.141 ns |     616 B |
| BuildCronTrigger   | 1,023.56 ns | 144.541 ns |    1824 B |

### Scheduling into RAMJobStore

`ScheduleJobBenchmark`, BenchmarkDotNet's default job. One invocation is 50,000 schedules into a
store that started empty, each under a fresh identity, through a scheduler that has been started.

| Method                    | Mean      | Error     | Allocated |
|-------------------------- |----------:|----------:|----------:|
| ScheduleJob_SimpleTrigger |  4.610 us | 0.1630 us |   3.06 KB |
| ScheduleJob_CronTrigger   | 10.479 us | 0.7597 us |      5 KB |

Against the published 3.14 table: simple 4.4 µs / 2.3 KB, cron 31 µs / 38.7 KB.

**These two rows were taken with unequal fixtures**, which #3802 found and fixed: the cron arm put one
entry in the job data map and the simple arm put none, so part of the gap between them was a
dictionary rather than a schedule. Both arms carry the entry now, and the `ScheduleJob_SimpleTrigger`
row above is therefore the older, lighter fixture. The cron row is unaffected.

### The repository's own cron suite

`CronExpressionBenchmark` over all fourteen expression shapes it carries, `--job Short`. Three
iterations on a loaded machine is enough to place a mean but not to split two of them, so read the
Error column here as "same order of magnitude" and nothing finer. The `Allocated` column is exact
regardless, and it is the point: **every** `NextOccurrence` and `NextOccurrences100` row is `-`,
including the `L`, `L-2`, `LW`, `6#3` and `6L` shapes whose day-of-month work used to be the
expensive one.

| Method             | CronExpression       | Mean         | Error         | Allocated |
|------------------- |--------------------- |-------------:|--------------:|----------:|
| Parse              | 0 0 12 * * ?         |     245.0 ns |     274.62 ns |     576 B |
| NextOccurrence     | 0 0 12 * * ?         |     501.4 ns |     278.06 ns |         - |
| NextOccurrences100 | 0 0 12 * * ?         |  45,163.9 ns |  50,338.57 ns |         - |
| Parse              | 0 0 8-18 ? * MON-FRI |     338.8 ns |     494.27 ns |     656 B |
| NextOccurrence     | 0 0 8-18 ? * MON-FRI |     462.5 ns |      49.64 ns |         - |
| NextOccurrences100 | 0 0 8-18 ? * MON-FRI |  40,397.3 ns |  12,458.01 ns |         - |
| Parse              | 0 0-30 9-17 * * ?    |     314.9 ns |      43.40 ns |     800 B |
| NextOccurrence     | 0 0-30 9-17 * * ?    |     373.8 ns |      46.31 ns |         - |
| NextOccurrences100 | 0 0-30 9-17 * * ?    |  31,134.5 ns |   5,861.73 ns |         - |
| Parse              | 0 0,1(...)* * ? [26] |     367.2 ns |     994.21 ns |     576 B |
| NextOccurrence     | 0 0,1(...)* * ? [26] |     509.0 ns |      57.06 ns |         - |
| NextOccurrences100 | 0 0,1(...)* * ? [26] |  46,529.6 ns |  79,803.42 ns |         - |
| Parse              | 0 0/5 * * * ?        |     271.9 ns |     690.19 ns |     576 B |
| NextOccurrence     | 0 0/5 * * * ?        |     531.6 ns |   1,065.92 ns |         - |
| NextOccurrences100 | 0 0/5 * * * ?        |  56,734.7 ns |  60,939.11 ns |         - |
| Parse              | 0 15 10 ? * 6#3 *    |     398.3 ns |     450.21 ns |     504 B |
| NextOccurrence     | 0 15 10 ? * 6#3 *    |   1,049.1 ns |     975.28 ns |         - |
| NextOccurrences100 | 0 15 10 ? * 6#3 *    | 137,298.6 ns | 178,941.86 ns |         - |
| Parse              | 0 15 10 ? * 6L       |     362.1 ns |     390.62 ns |     504 B |
| NextOccurrence     | 0 15 10 ? * 6L       |   1,133.3 ns |     419.70 ns |         - |
| NextOccurrences100 | 0 15 10 ? * 6L       | 149,851.9 ns | 100,250.02 ns |         - |
| Parse              | 0 15 10 * * ?        |     267.0 ns |     161.66 ns |     576 B |
| NextOccurrence     | 0 15 10 * * ?        |     558.3 ns |     483.28 ns |         - |
| NextOccurrences100 | 0 15 10 * * ?        |  54,941.0 ns |   1,490.88 ns |         - |
| Parse              | 0 15 (...)-2025 [23] |     673.4 ns |   1,331.95 ns |    1720 B |
| NextOccurrence     | 0 15 (...)-2025 [23] |     449.1 ns |      55.71 ns |         - |
| NextOccurrences100 | 0 15 (...)-2025 [23] |  46,769.5 ns |  17,380.83 ns |         - |
| Parse              | 0 15 (...)* ? * [35] |     385.1 ns |     445.59 ns |     576 B |
| NextOccurrence     | 0 15 (...)* ? * [35] |     583.5 ns |      88.37 ns |         - |
| NextOccurrences100 | 0 15 (...)* ? * [35] |  79,317.9 ns |  85,571.17 ns |         - |
| Parse              | 0 15 10 L * ?        |     288.7 ns |     532.19 ns |     632 B |
| NextOccurrence     | 0 15 10 L * ?        |     915.6 ns |   1,367.75 ns |         - |
| NextOccurrences100 | 0 15 10 L * ?        |  98,619.6 ns |  78,363.64 ns |         - |
| Parse              | 0 15 10 L-2 * ?      |     394.8 ns |     355.85 ns |     752 B |
| NextOccurrence     | 0 15 10 L-2 * ?      |   1,098.9 ns |     491.07 ns |         - |
| NextOccurrences100 | 0 15 10 L-2 * ?      | 133,915.8 ns | 101,947.05 ns |         - |
| Parse              | 0 15 10 LW * ?       |     379.8 ns |     678.10 ns |     640 B |
| NextOccurrence     | 0 15 10 LW * ?       |     991.0 ns |     553.20 ns |         - |
| NextOccurrences100 | 0 15 10 LW * ?       | 109,018.2 ns |  30,428.74 ns |         - |
| Parse              | 0/15 * * * * ?       |     288.8 ns |     145.36 ns |     688 B |
| NextOccurrence     | 0/15 * * * * ?       |     299.9 ns |      18.89 ns |         - |
| NextOccurrences100 | 0/15 * * * * ?       |  43,571.4 ns | 173,050.96 ns |         - |

### Verdict

The Quartz 3.14 numbers no longer describe `main`: `GetNextValidTimeAfter` now allocates nothing at
all rather than ~3 KB a call and runs in 315-373 ns rather than ~1.2 µs, constructing a
`CronExpression` costs 236-338 ns and 576-688 B rather than 3-4.6 µs and 8.7-12.9 KB — which makes
Quartz's parse now faster than NCrontab's — and scheduling a cron-triggered job into `RAMJobStore`
costs 10.5 µs and 5 KB rather than 31 µs and 38.7 KB; the one claim that survives is that NCrontab
computes a next occurrence about 14× faster than Quartz (25 ns against 362 ns), which is a real gap
but not the 88× the published table reports.

### What the remaining numbers are made of

Recorded rather than acted on — #3538 asks for the measurement, not for an optimisation.

- **`GetNextValidTimeAfter` allocates nothing.** The `Allocated` column is `-` for every
  next-fire-time case, including the hundred-call one that the published table puts at 314 KB.
  `CronExpression.GetTimeAfter` walks with a `readonly record struct NextFireTimeCursor` and the
  field progressors are called directly rather than through a delegate array, so the ~3 KB a call is
  gone. There is no allocation left to hunt, and no follow-up issue is owed for one.
- **`WithCronSchedule(string)` parses the expression twice.** `CronScheduleBuilder.Create(string)`
  called the since-removed `CronExpression.ValidateExpression(cronExpression)` — whose whole body was
  `var _ = new CronExpression(cronExpression);` — and then handed the same string to
  `CronScheduleNoParseException`, which constructed a second one. Two parses at 307 ns and 576 B each
  account for most of `BuildCronTrigger`'s 1,024 ns and 1,824 B, against `BuildSimpleTrigger`'s 98 ns
  and 616 B. `CronTriggerImpl.GetScheduleBuilder()` goes down the same path.
  **Fixed in #3542** — see the next section.
- **Most of the cron/simple gap in `ScheduleJob` is not cron.** The two cases differ by 5.9 µs, of
  which the build accounts for 0.9 µs. The rest is a property of the fixtures rather than of cron
  parsing: every one of the 50,000 cron triggers in an invocation is `0 0/5 * * * ?`, so they all
  land on the *same* next fire time, while the simple triggers get `UtcNow + 30 s` and so are
  distinct and increasing. `TriggerTimeComparator` returns on the `DateTimeOffset` comparison for the
  simple ones; for the cron ones it falls through equal times and equal priorities to
  `trig1.Key.CompareTo(trig2.Key)`, which ends in `StringComparer.Ordinal.Compare` on the trigger
  name — a string comparison per level of the store's `SortedSet` on every insert. The published
  comparison uses the same single cron expression, so its 31 µs carries the same effect.
  **The mechanism is real; the size was a guess and it was wrong** — #3542 measured it at about a
  tenth of a microsecond a schedule, not microseconds. See the next section.

## What #3542 changed (2026-08-31, same machine)

Same box and runtime as above. The `ScheduleJobBenchmark` rows are not repeated here: on the day
these were taken its `ScheduleJob_SimpleTrigger` row — which no part of #3542 can touch — read
between 3.8 and 6.8 µs across four alternating runs, so that suite's `Mean` column was measuring the
machine rather than the change. The suites below are tight enough to read.

### A cron schedule is parsed once

`JobAndTriggerBuilderBenchmark`, default job, alternating before/after runs.

| Method                         |               Before |                After |
|------------------------------- |---------------------:|---------------------:|
| BuildCronTrigger               | 607-628 ns / 1,824 B | 365-385 ns / 1,248 B |
| ReadCronScheduleBackOffTrigger |     678 ns / 1,776 B |        6.4 ns / 48 B |
| BuildSimpleTrigger (control)   |    90-97 ns /  616 B |    90-97 ns /  616 B |

`BuildCronTrigger` loses exactly one parse — 576 B, the `Parse_Simple` row above.
`ReadCronScheduleBackOffTrigger` is new, and it loses both: a trigger already holds its parsed,
immutable expression, so `GetScheduleBuilder` hands that instance over instead of sending the string
back through the parser twice.

### Equal fire times still compare names, deliberately

`TriggerTimeComparatorBenchmark`'s sorted-insert cases, default job. One operation is one insert into
a `SortedSet` that ends up holding 50,000 triggers, which is the depth `RAMJobStore` reaches in
`ScheduleJobBenchmark`.

| Method                                | Mean      | Allocated |
|-------------------------------------- |----------:|----------:|
| SortedInsert_DistinctFireTimes        |  73.07 ns |      48 B |
| SortedInsert_OneFireTime              | 173.97 ns |      48 B |
| SortedInsert_OneFireTime_HashTieBreak | 237.86 ns |      48 B |

Sharing a fire time costs about 100 ns an insert, and #3542's proposed cure — tie-breaking on the
key's cached hash before the name — costs 64 ns more than the disease. Ordering by hash scatters keys
that the ordinal order keeps adjacent, so the tree walk it lengthens costs more than the string
comparison it skips; and a string's hash is seeded per process, so the order would stop being the
same order twice. The tie-break stays the key.

## Fire throughput and per-fire allocation (2026-09-02, AMD Ryzen 9 5950X)

Taken on `1e6af15e1` to answer #3653, and the `RAMJobStore` half re-taken after #3676 - see that
subsection, which says what moved and why. The numbers already in this file are about parsing an
expression and putting a trigger into a store; this section is about the thing a production reader
actually asks - **how many trigger firings a second, and how much garbage per firing** - and it is
the first time 4.0 has had one. It is also where the migration guide's claim that the batched fire
path replaced "six to nine round trips" with one finally gets a figure.

`FireThroughputBenchmark` and `FireThroughputPostgresBenchmark` are the harness, and
`baseline-3x/FireThroughputBaselineBenchmark.cs` is the 3.x half of the comparison - the same
workload and the same settings written against 3.x's API, so both sides came off one machine in one
sitting. `baseline-3x/README.md` says how to re-run it.

**How to read the table.** One operation is one firing, so `Mean` is the time a firing took and fires
per second is `1e9 / Mean(ns)`. `Allocated` is process-wide over the measured window rather than
per-thread - BenchmarkDotNet reads `GC.GetTotalAllocatedBytes` - so it is what a firing costs the
process, acquisition loop and worker threads included, not what one thread of it cost.

**Machine and runtime.** BenchmarkDotNet v0.15.8; Windows 11 (10.0.26200.9168/25H2); AMD Ryzen 9
5950X 3.40 GHz, 1 CPU, 32 logical and 16 physical cores; .NET SDK 10.0.400; host and job
.NET 10.0.11 (10.0.1126.37416), X64 RyuJIT x86-64-v3, Concurrent Workstation GC. PostgreSQL 15.1 in
Docker Desktop, reached over loopback, at its shipped durability settings (`fsync = on`,
`synchronous_commit = on`). The 3.x rows were taken on `origin/3.x` at `b33c70487`, against a
database built from **3.x's own** `database/tables/tables_postgres.sql` rather than 4.0's; the
re-taken `RAMJobStore` rows are `origin/3.x` at `9ee33fec1` against `main` at `e74dd6345` plus #3676.

**Read the Error column.** This is a working machine and something else is usually running on it, so
the means carry more spread than a dedicated box would give. The PostgreSQL sitting came out tight -
every Error there is under 1.5% of its Mean, which is unlike the older sections in this file - and
that is what lets its 3.x-to-4.0 ratios be read as ratios rather than as directions. The
`RAMJobStore` sitting did not: it was re-run under load, so those rows carry ranges over five
alternating pairs and Errors up to 10%, and the safe reading of them is the medians and the
allocation column. The allocation column is exact whatever the load, and the effects below are
multiples rather than percentages.

**Two settings are not at their defaults, and both had to move.** `MaxBatchSize` tracks
`MaxConcurrency` - the scheduler refuses a batch larger than the pool that would have to run it, so
across a 10-and-50 sweep the two cannot be varied independently, and the batch is the pool.
`BatchTriggerAcquisitionFireAheadTimeWindow` is **one second** rather than the shipped zero: the
store ends a batch at the first acquired trigger's own fire time plus the window, so at the default a
batch is one trigger however large `MaxBatchSize` is. A deployment left at the shipped defaults gets
the batch-of-one shape, which is one acquisition round trip per firing.

**The workload.** Two thousand simple triggers over a hundred jobs, repeating indefinitely every
millisecond under the ignore-misfires instruction, over a job that counts and returns. Every trigger
is therefore permanently overdue, so the scheduler never waits and what is measured is the fire path
rather than the clock. Two thousand of them put the arrangement's own ceiling at two million firings
a second, several times what the fastest arm here reaches.

**One node, and not clustered.** What is measured is acquire, fire, complete. Clustering adds a
check-in loop, a cluster-wide lock on every acquisition cycle and a second node competing for the
same rows; those are real costs and none of them is in these numbers.

### RAMJobStore

**Re-measured on 2026-09-02 after #3676, which is what #3674 turned into.** The first sitting had 4.0
at 3.4 us and 3.62 KB a firing against 3.20's 2.3 us and 3.25 KB; the rows below replace it. Both arms
were re-run in one sitting on this machine, **five alternating pairs**, the order reversed between
them, and the machine was busy throughout - so these are ranges over the five runs rather than the one
figure a quiet box would give, and the medians are what the verdict below reads.

| Version | MaxConcurrency | Mean (5 runs)   | Median   | Error       | Allocated       | Fires/second      |
|-------- |--------------- |---------------: |--------: |-----------: |---------------: |-----------------: |
| 4.0     | 10             | 1.865-2.604 us  | 2.164 us | 1.6%-2.7%   | 2.56-2.58 KB    | 384,000-536,000   |
| 4.0     | 50             | 1.347-2.102 us  | 1.805 us | 0.9%-7.8%   | 2.56-2.57 KB    | 476,000-742,000   |
| 3.20    | 10             | 2.456-3.237 us  | 2.579 us | 1.7%-6.0%   | 3.21-3.28 KB    | 309,000-407,000   |
| 3.20    | 50             | 2.234-4.382 us  | 2.713 us | 1.6%-10.5%  | 3.25-3.30 KB    | 228,000-448,000   |

The allocation column is the one to trust without qualification: four of the five 4.0 runs read
2.56 KB or 2.57 KB at both pool sizes and the fifth read 3.35 KB at `MaxConcurrency` 50 with an 8%
Error, which is the only reading in the set that does not repeat.

### PostgreSQL

| Version | MaxConcurrency | Mean     | Error     | Allocated | Fires/second |
|-------- |--------------- |---------:|----------:|----------:|-------------:|
| 4.0     | 10             | 6.177 ms | 0.0911 ms |  56.47 KB |          162 |
| 4.0     | 50             | 6.129 ms | 0.0638 ms |  52.78 KB |          163 |
| 3.20    | 10             | 9.865 ms | 0.1322 ms | 136.09 KB |          101 |
| 3.20    | 50             | 9.212 ms | 0.1014 ms | 132.10 KB |          109 |

### Verdict

**On a persistent store, 4.0 is 1.5x faster per firing than 3.20 and allocates 2.4x less** - 6.1 ms
against 9.2-9.9 ms, and 53-56 KB against 132-136 KB. That is the batched fire path, and it is the
number the migration guide's round-trip claim never had. Counted at the database rather than in the
client, a firing now costs **1.27 commits** - 42,337 transactions over 33,404 firings in one run -
which is one `TriggeredJobComplete` plus a share of an acquisition and a `TriggersFired` amortised
across the batch.

**On `RAMJobStore`, 4.0 is faster per firing than 3.20 and allocates 21% less** - 2.56 KB against
3.25 KB, and 2.16 us against 2.58 us by the medians at `MaxConcurrency` 10, 1.81 us against 2.71 us at
50. It was not, until #3676. The first sitting had it 1.4x *slower*, which is what #3674 was filed to
explain, and the explanation was not the list this paragraph used to carry - a DI scope per firing,
the middleware pipeline, the execution-group ledger, the retry-policy check. Measured one at a time,
every one of those is *cheaper* than the 3.x code it replaced or costs nothing at all: the scope 328
bytes against 456, the execution context 248 against 480, the listener notifications 48 against 720,
and the pipeline, the activity, the meters and the retry check zero apiece. The regression was two
things in the store, both fixed in #3676: a `Dictionary` allocated and thrown away on every firing to
hold the trigger's running executions, and a lock taken with `await SemaphoreSlim.WaitAsync` where
3.x's is a monitor, which made 64% of `TriggersFired` calls, 41% of `TriggeredJobComplete` calls and
31% of acquisitions suspend and cost a thread-pool hop each. The full attribution is on #3674.

**Pool size does not move either store much, and on PostgreSQL it does not move it at all.** Five
times the threads buys about 17% on 4.0's `RAMJobStore` by the medians above, nothing on 3.20's, and
nothing measurable on PostgreSQL, because the store's own operations serialise on one lock - the
trigger-access lock on a persistent store, the monitor on the in-memory one. What a bigger pool buys
is more *jobs* running at once, not more firings being started. This is the same fact `operations.md`
states as "adding nodes does not make a single trigger fire faster", measured.

**The PostgreSQL figure is a commit-latency figure as much as a Quartz one.** Re-running the 4.0 arm
with `synchronous_commit = off` gives 3.605 ms at `MaxConcurrency` 10 and 4.140 ms at 50 - 1.7x
faster, so roughly 40% of the 6.1 ms is this container's fsync. A deployment on faster storage will
see more firings a second than the table says; one on slower storage will see fewer. Read the
3.20-to-4.0 ratio, which is taken on one machine and one database, rather than the absolute.

### A finding, filed rather than fixed

Writing this benchmark surfaced one: **a simple trigger with a sub-millisecond repeat interval is
silently broken by any ADO store.** `StdAdoDelegate.GetDbTimeSpanValue` casts `TotalMilliseconds` to
`long`, so an interval below a millisecond persists as **zero**; `SimpleTriggerImpl.GetFireTimeAfter`
then divides by `repeatInterval.Ticks` and throws `DivideByZeroException` on the trigger's next
firing, which `AdoJobStoreBase` logs and swallows - leaving the row in `ACQUIRED` for good. Nothing
surfaces it, and `RAMJobStore` keeps the interval, so the two stores disagree. Filed as #3673. The
workload above uses a millisecond because that is the smallest interval both stores agree on.

## What #3801 changed (2026-09-19, AMD Ryzen 9 5950X)

`CronExpression.GetNextValidTimeAfter` now computes the steady-state answer without asking
`TimeZoneInfo` anything: an integer walk over the field bitmasks, at an offset read from a per-zone
table of *safe segments*, falling back to the unchanged search - renamed `GetTimeAfterSlow` - wherever
the answer cannot be proved to be the same one. The profile behind it is on #3802: 44.6% of on-CPU in
`Next100` was a `TimeZoneInfo` frame, and an A/B there priced a daylight-saving zone at 457 ns against
157 ns for a zone that never moves its clocks.

**Machine and runtime.** BenchmarkDotNet v0.15.8; Windows 11 (10.0.26200.9457/25H2); AMD Ryzen 9
5950X 3.40 GHz, 1 CPU, 32 logical and 16 physical cores; .NET SDK 10.0.401; host and job .NET 10.0.12
(10.0.1226.42308), X64 RyuJIT x86-64-v3; Workstation concurrent GC. `--job short --maxIterationCount
20`. The machine's local time zone is **`FLE Standard Time`** (UTC+02:00, observes daylight saving),
which is the zone every row below resolves against - `CronExpressionComparisonBenchmark` and
`CronExpressionBenchmark` both leave the zone unset, so both get the local one, and it is the zone
that decides whether the search pays for daylight saving at all. On a fixed-offset zone the before
column would read about 160 ns rather than about 420.

**Read the ranges, not the means.** Other work was on the machine throughout. The two
`CronExpressionComparisonBenchmark` arms were run in one sitting as **five alternating pairs, the
order reversed between them**, so the medians and ranges below are over five runs each; the other
three suites are one run per arm and are directions rather than measurements.
`TriggerTimeComparatorBenchmark` is the control - nothing in this change can touch it - and it reads
59.8 ns against 59.9 ns, 159.7 against 159.8, 2.0 ns against 2.0 ns on the `CompareTo` cases.

### The cross-library comparison

`CronExpressionComparisonBenchmark`, medians of five runs per arm, with the five-run range beside
them. The NCrontab column is from the after sitting and is its own control: it reads within a
nanosecond of the before sitting's, so the two arms were measured on the same machine in the same mood.

| Method            |               Before |                After |         NCrontab |
|------------------ |---------------------:|---------------------:|-----------------:|
| Next_Simple       |    420.7 (412-428) ns |    **36.5** (35-44) ns |          25.3 ns |
| Next_Complex      |    472.0 (432-546) ns |    **46.2** (46-55) ns |          21.8 ns |
| Next_SecondLevel  |    353.1 (348-368) ns |    **30.9** (30-36) ns |          40.6 ns |
| Next100           | 43,132 (41,825-43,593) ns | **3,521** (3,379-3,640) ns |       2,671 ns |
| Parse_Simple      |    212.2 ns / 496 B  |    218.6 ns / 504 B  | 328.6 ns / 1816 B |
| Parse_Complex     |    295.4 ns / 648 B  |    291.4 ns / 656 B  | 365.6 ns / 1880 B |
| Parse_SecondLevel |    233.5 ns / 608 B  |    219.6 ns / 616 B  | 375.9 ns / 2152 B |

Every `Next*` row allocates nothing, before and after. **A next occurrence is 11.5x faster and a
hundred of them 12.3x**, which puts Quartz at 1.44x NCrontab on the single call, 1.32x on the chain,
and *faster* than it on the second-level expression - which is the one case where NCrontab has the
same work to do and no bitmask to do it with.

**Parsing costs eight bytes more and no measurable time.** The eight is the one field the fast path
adds to `CronExpression`: the zone it last computed against, bound to that zone's offset tables. The
parse times move by less than the five-run ranges of the rows themselves.

### The repository's own cron suite

`CronExpressionBenchmark`, all fourteen shapes, one run per arm. `NextOccurrence` from a fixed start
in June 2005 - so these are single calls rather than chains, which is the shape the acceptance numbers
are stated in. Every row's `Allocated` is `-` in both arms.

| CronExpression           | NextOccurrence before | NextOccurrence after | NextOccurrences100 before | after |
|------------------------- |---------------------:|--------------------:|-------------------------:|------:|
| `0 0 12 * * ?`           |             431.9 ns |        **33.4 ns**  |              43,699 ns |  4,281 ns |
| `0 0 8-18 ? * MON-FRI`   |             560.6 ns |        **43.0 ns**  |              51,510 ns |  4,691 ns |
| `0 0-30 9-17 * * ?`      |             507.1 ns |        **33.6 ns**  |              41,310 ns |  3,496 ns |
| `0 0,10,20,30,40,50 ...` |             477.6 ns |        **30.2 ns**  |              43,349 ns |  3,420 ns |
| `0 0/5 * * * ?`          |             483.4 ns |        **34.2 ns**  |              43,304 ns |  3,436 ns |
| `0 15 10 ? * 6#3 *`      |             779.0 ns |        **37.4 ns**  |             107,494 ns |  6,991 ns |
| `0 15 10 ? * 6L`         |             796.2 ns |        **37.1 ns**  |             106,173 ns | 29,104 ns |
| `0 15 10 * * ?`          |             527.3 ns |        **34.6 ns**  |              50,860 ns |  4,062 ns |
| `0 15 10 * * ? 2005-2025`|             489.4 ns |        **43.7 ns**  |              49,987 ns | 10,362 ns |
| `0 15 10 1,2,3,... * ?`  |             485.5 ns |        **36.7 ns**  |              71,728 ns |  7,632 ns |
| `0 15 10 L * ?`          |             784.5 ns |        **36.0 ns**  |              79,998 ns | 17,460 ns |
| `0 15 10 L-2 * ?`        |             792.4 ns |        **38.3 ns**  |             108,105 ns | 31,105 ns |
| `0 15 10 LW * ?`         |             793.8 ns |        **37.6 ns**  |              91,326 ns | 19,195 ns |
| `0/15 * * * * ?`         |             357.9 ns |        **31.4 ns**  |              38,321 ns |  3,154 ns |

**The `L`, `W`, `#` and `nL` shapes are no longer the expensive ones.** They cost 779-796 ns before
and 36-38 ns after, because the walk resolves them per month out of `CalculateDaysOfMonth` and a
single bit, rather than through the `DateTimeOffset`-rebuilding day progressors.

**The hundred-call rows say where the fast path stops, and it is not a defect.** A chain of a hundred
fires of a *monthly* expression covers eight years, and eight years of a daylight-saving zone contains
sixteen transitions, each of which costs four days of fast path and is answered by the search that was
always there. `0 15 10 ? * 6L` is 3.6x rather than 12x for exactly that reason; `0 0/5 * * * ?`, whose
hundred fires span eight hours, is 12.6x. A running trigger asks for one fire time at a time, near
now, which is the `NextOccurrence` column.

### Scheduling a job is unmoved

`ScheduleJobBenchmark`, one run per arm, fixtures already equalised by #3802.

| Method                    |  Before |   After |
|-------------------------- |--------:|--------:|
| ScheduleJob_SimpleTrigger | 5.86 µs / 3.81 KB | 6.29 µs / 3.81 KB |
| ScheduleJob_CronTrigger   | 7.45 µs / 4.49 KB | 7.33 µs / 4.52 KB |

The simple arm is the control here and it moved 0.4 µs between two runs, which is the size of this
suite's noise on a loaded machine; the cron arm moved 0.1 µs the other way. The 30 bytes the cron arm
gained are the same eight-byte field as above, rounded by the KB column. Scheduling is dominated by
the store, the listener machinery and the scheduler-thread wake, none of which this touches - the
cron work inside it is under a microsecond, which #3802 measured.


## Against TickerQ and Hangfire (2026-09-19, AMD Ryzen 9 5950X)

Taken on `89fbadcdc2` to answer #3802's D2, with the harness in
[`src/Quartz.Benchmark.Competitors`](../Quartz.Benchmark.Competitors/README.md) — a project outside
`Quartz.slnx` that references `Quartz.csproj`, so what these rows measure is the working tree rather
than a published package. The sections above compare 4.0 against 3.x and against NCrontab. This one
compares it against the two other .NET schedulers it gets put beside: **TickerQ 10.4.0** and
**Hangfire 1.8.25**.

**What makes these rows different from the published comparison they answer.** Every arm here
executes a job, and every arm counts inside the executing job: an `Interlocked` counter incremented
by the job body, an absolute target published before the work is scheduled, and a wait with a timeout
that turns a hang into an exception. A benchmark invocation is *waiting for N executions to happen*.

**What TickerQ's own suite measures.** `benchmarks/TickerQ.Benchmarks/Comparisons/` at 10.4.0:
`ConcurrentThroughputComparison`'s baseline arms are a `FrozenDictionary<string,
TickerFunctionDelegate>` lookup followed by invoking a delegate whose body is `Task.CompletedTask`;
the Hangfire arms beside them create a background job in storage; the Quartz arms build an
`IJobDetail` and an `ITrigger` and call `ScheduleJob`. `JobCreationComparison`'s baseline is
"TickerQ: FrozenDictionary function lookup". The arms run under `Parallel.For` with
`.GetAwaiter().GetResult()`. The project pins `Quartz` `3.14.*` and reaches it through
`new StdSchedulerFactory()`. Nothing clears the scheduler or the storage between iterations, and the
counter that names each job increments for the length of the run. No job is executed on any side.

**Machine and runtime.** BenchmarkDotNet v0.15.8; Windows 11 (10.0.26200.9457/25H2); AMD Ryzen 9
5950X 3.40 GHz, 1 CPU, 32 logical and 16 physical cores; .NET SDK 10.0.401; host and job .NET 10.0.12
(10.0.1226.42308), X64 RyuJIT x86-64-v3, Concurrent Workstation GC. PostgreSQL 15.1 in Docker over
loopback at its shipped durability settings (`fsync = on`, `synchronous_commit = on`), started with
`-c shared_preload_libraries=pg_stat_statements` so the statement census could be taken.

**Read the Error column.** This is a working machine and other sessions were building and testing
throughout. The in-memory rows below are ranges over **three sittings, the middle one run with the
arms in reverse order**, and the safe reading of them is the ratio between rows rather than the
absolute of any one. The `Allocated` column is exact whatever the load, and the database census is
counted at the database rather than in the client, so both of those carry across.

**The settings, and the one that matters most.** `MaxConcurrency` is **10** on all three — Quartz's
shipped default, against TickerQ's `Environment.ProcessorCount` (32 here) and Hangfire's
`ProcessorCount × 5` (160). Left alone, Hangfire would have run this workload with sixteen times
Quartz's workers. TickerQ's `MinPollingInterval` is 100 ms rather than its default second, and
Hangfire's `SchedulePollingInterval` is 50 ms in memory and 100 ms on PostgreSQL rather than its
default fifteen seconds; both are the settings #3802 specified, and both favour the library they are
set on. Everything else on all three sides is the shipped default. Quartz appears twice because its
two shapes differ: `defaults` is `MaxBatchSize` 1 with no fire-ahead window, which is what
`AddQuartz(q => q.UseInMemoryStore())` gives you, and `tuned` is the batch tracking the pool with a
one-second window, which is what the fire-throughput section above was taken at.

### S1 — in-memory throughput

Twenty thousand one-off schedules, all due at the same whole second two seconds out, over a job that
increments a counter and returns. The measured window is the drain: the engine is built and the work
scheduled in the iteration setup, and the body waits for twenty thousand executions, so `Mean` is
what one of them cost. Two seconds puts every arm on its scheduler's own path — TickerQ dispatches a
ticker due within one second on the calling thread, and Hangfire's `Enqueue` skips its delayed-job
poll — and both of those short-circuits get rows of their own.

| Engine | Mean (3 sittings) | Allocated | Executions/second |
|------- |-----------------: |---------: |-----------------: |
| Quartz (defaults)    |  3.89-5.54 µs |  3.46 KB |  181,000-257,000 |
| Quartz (tuned)       |  5.51-6.57 µs |  3.26 KB |  152,000-181,000 |
| TickerQ              |  8.68-10.25 µs |  4.92-5.39 KB |   98,000-115,000 |
| Hangfire (scheduled) | 14.90-17.12 µs | 24.29-24.39 KB |   58,000-67,000 |
| Hangfire (enqueued)  | 10.83-15.07 µs | 19.24 KB |   66,000-92,000 |

**Quartz is the fastest of the three here and allocates the least**, by about 2× against TickerQ and
about 3× against Hangfire on time, and by 1.5× and 7× on bytes. The allocation column is the one to
read without qualification.

**Quartz's tuned profile is consistently a little slower than its defaults on this workload**, in all
three sittings, while allocating slightly less — 3.26 KB against 3.46 KB, which is the acquisition
rounds it saves. Twenty thousand triggers due at one instant is the shape that batching should help
most, so this is worth saying plainly: at ten workers the fire path is contention-bound, which is
what #3802's D1 profile found (36.9 s of lock-blocked thread time against 29.8 s on CPU), and holding
the store's lock for ten triggers instead of one does not help a workload whose limit is that lock.
The Errors here are large; the ordering is the part that repeated.

### S2 — PostgreSQL throughput

The same, two thousand deep, against one PostgreSQL database with each library in its own schema at
its own default — Quartz in `public`, TickerQ in `ticker`, Hangfire in `hangfire`. One database means
one `fsync` setting, one disk and one connection pool under all three. The lead time is twenty seconds
rather than two, because two thousand schedules against a database is seconds of writing on every arm
— see the note under the table. Five iterations rather than seven; the store is emptied between them.

| Engine | Mean (2 sittings) | StdDev | Allocated | Executions/second |
|------- |-----------------: |------: |---------: |-----------------: |
| Quartz (defaults) | 11.30-11.77 ms | 0.20-0.74 ms |  89.9 KB |   85-89 |
| Quartz (tuned)    | 10.18-10.47 ms | 0.34-1.24 ms |  76.5 KB |   96-98 |
| TickerQ           |  2.91-3.15 ms | 0.03-0.34 ms |  48.7-50.0 KB | 317-344 |
| Hangfire          | 15.78-15.84 ms | 0.65-0.66 ms | 101.5 KB |      63 |

**Quartz loses this one to TickerQ by 3.5-4x**, and beats Hangfire by about 1.4x. The row below says
where the difference is: TickerQ's execution costs the database about two statements and Quartz's
about twenty.

**Here the tuned profile is the faster of the two Quartz rows**, which is the opposite of what
happened in memory — 10.2-10.5 ms against 11.3-11.8, and 76.5 KB against 89.9. On a database an
acquisition round is round trips and a commit, so amortising it across a batch of ten buys something
real; in memory it is a lock the workload is already waiting on.

**The two sittings agree.** Every arm's two means are within 8 % of each other and the ordering is
identical in both, which is more than the in-memory rows can say. The `Error` column is half a 99.9 %
confidence interval over five iterations and is wide here, so `StdDev` is above instead. These are
still a commit-latency figure as much as a scheduler one: on faster storage every row moves, and the
ratios between them are what carries.

One run is not in the table. The guard that fails a sitting whose scheduling overran the lead time
refused the second Quartz-defaults sitting at a ten-second lead — writing two thousand job details and
two thousand triggers took longer than that on a busy box — so the lead is twenty seconds in the
committed harness and that arm was re-run at it. The lead is spent in the iteration setup and is
outside every measured window; the first sitting's rows were taken at ten.

### S2 — what one execution costs the database

Counted at the database rather than in the client, over one complete window per engine — the drain
plus a three-second tail, so that the last execution's own completion write is inside it. The idle
column is the same engine, still running, with nothing left to do.

| Engine | Window (s) | Commits | Commits/execution | Statements | Statements/execution | Idle statements/s |
|------- |----------: |-------: |----------------: |---------: |-------------------: |----------------: |
| Quartz (defaults) | 26.6 |  11,724 |  **5.86** |  51,999 | **26.00** |   0.2 |
| Quartz (tuned)    | 23.7 |   5,466 |  **2.73** |  39,173 | **19.59** |   0.2 |
| TickerQ           |  9.0 |     415 |  **0.21** |   3,913 |  **1.96** |   0.2 |
| Hangfire          | 33.3 |  58,850 | **29.43** | 123,135 | **61.57** | 526.0 |

**TickerQ costs the database an order of magnitude less than Quartz here, and half of why is that it
deletes nothing.** Its acquisition marks a whole second's worth of tickers in one bulk statement, so
the only per-row write is the completion — and the completed ticker stays in the table afterwards.
That is the trade: the cheapest execution of the three, and a table that only grows.

**Quartz's figure is a one-off schedule's, which is its most expensive kind.** Completing one deletes
the trigger row, the simple-trigger row and — because the job detail has no other trigger and is not
durable — the job detail too. The fire-path figure #3802's D1 report measured on a *repeating*
trigger is 9.7 statements and 1.24 commits an execution, against 19.6 and 2.73 here; the difference
is the deletion. The defaults profile pays an acquisition round and a `TriggersFired` per execution
on top of that, where the tuned profile amortises both across a batch of ten — which is also why
batching is worth something on a database and was worth nothing in memory.

**Hangfire's figure is the largest, and part of it is the server watching rather than working.** An
idle Hangfire server at these settings issues about 526 statements a second — ten workers polling a
queue at `QueuePollInterval`, plus the delayed-job and recurring-job schedulers — so over a 33-second
window roughly a seventh of its statements are polls. The rest is its state machine: every
transition through `Enqueued`, `Processing` and `Succeeded` is a storage write with a state-history
entry. Quartz's and TickerQ's idle rates are indistinguishable from zero, because both sleep until
the next fire time rather than polling for it.

### S3 — schedule-to-execute latency

One job at a time on an idle engine, two hundred repetitions, twenty milliseconds of quiet between
them so that every repetition begins with the engine's loop parked. Timestamps are
`Stopwatch.GetTimestamp()` at the call and again as the job's first instruction, so these stop where
the job starts. Three sittings, the middle one in reverse order.

| Engine | p50 | p95 | p99 |
|------- |----: |----: |----: |
| Quartz, `StartNow`             |  58-70 µs |  169-233 µs |  261-441 µs |
| Hangfire, `Enqueue`            |  94-235 µs |  291 µs-14.5 ms |  1.0-22.5 ms |
| TickerQ, `ExecutionTime = null` | 14.7-14.9 ms | 15.3-15.4 ms | 15.6-16.0 ms |
| Hangfire, `Schedule(TimeSpan.Zero)` | 30.6-30.8 ms | 31.4-31.5 ms | 31.6-32.2 ms |

**Quartz is the fastest of the four by an order of magnitude**, and the row below it is the one that
needs explaining rather than this one. #3802's D1 probe put `StartNow` on an idle `RAMJobStore`
scheduler at 74 µs p50 on this machine; 58-70 µs here is the same number.

**TickerQ's immediate path is not slow to dispatch; it is slow to be picked up.** A ticker with a null
`ExecutionTime` is acquired and dispatched on the calling thread, never reaching the scheduler loop —
that is the fastest route it has. What it is dispatched *into* is `TickerQTaskScheduler`, whose worker
loop backs off with `await Task.Delay(Math.Min(consecutiveStealFailures * 2, 50))` once it has failed
to find work more than three times running. On an idle engine every worker is inside that delay, so
the work item waits for one to come out, and the distribution is tight: p50 and p99 are within 1.3 ms
of each other. A busy TickerQ would not show this; an idle one does, and "idle" is what this scenario
is.

**Hangfire's two rows are its poll, measured twice.** `Enqueue` puts the job where a worker is already
looking. `Schedule(TimeSpan.Zero)` puts it in the delayed set, where the `DelayedJobScheduler` finds
it on its next pass — so that row is bounded by `SchedulePollingInterval`, 50 ms here. Read the 30.7 ms
as "up to one poll interval" rather than as a figure: the scenario's twenty milliseconds of quiet and
the fifty of poll settle into a stable phase, which is why its spread is so small.

### S4 — recurring accuracy

A hundred schedules that each say "every second", for sixty seconds, on an idle engine. Not a
BenchmarkDotNet benchmark — `dotnet run … -- --recurring` — because what it measures is not how long
a call took. Quartz's and TickerQ's deviations are against what the engine hands the job
(`IJobExecutionContext.ScheduledFireTimeUtc`, `TickerFunctionContext.ScheduledFor`); Hangfire's are
against the nearest whole second, because a recurring job there is turned into an ordinary background
job and the occurrence it came from is rewritten to "now" before the job is created. Two sittings.

| Engine | Fired / expected | within ±50 ms | within ±250 ms | Max deviation |
|------- |----------------: |-------------: |--------------: |------------: |
| Quartz, simple trigger, defaults | 6,000 / 6,000 | 100 % | 100 % | 15-23 ms |
| Quartz, cron `* * * * * ?`, defaults | 6,000 / 6,000 | 100 % | 100 % | 15 ms |
| Quartz, simple trigger, fire-ahead 1 s | 5,999-6,000 / 6,000 | 98.2 % | 98.2 % | 999 ms |
| TickerQ, cron `* * * * * *` | 6,000 / 6,000 | 73-78 % | 100 % | 74-75 ms |
| Hangfire, cron `* * * * * *`, poll 1 s | 5,900-6,000 / 6,000 | 0-15 % | 21-71 % | 494-497 ms |

**All three fire the right number of times.** What differs is when. At its defaults Quartz put every
one of six thousand firings inside fifty milliseconds of its scheduled second, twice; TickerQ put all
of them inside 250 ms and about three-quarters inside 50 ms, which is its hundred-millisecond poll;
Hangfire scattered across half a second.

**The third row is Quartz's own trade-off, priced.** `BatchTriggerAcquisitionFireAheadTimeWindow` is
what lets a batch hold more than the triggers due at one instant, and the way it does that is by
firing the later ones at the earliest one's fire time — so a second of window is up to a second of
"early". It is the right setting for the throughput rows above and the wrong one for a punctual
schedule, and a deployment should not have both.

**Hangfire does honour a six-field cron expression** — `RecurringJobEntity.ParseCronExpression` hands
one to Cronos with `CronFormat.IncludeSeconds` — so this is not the minute-granularity row #3802
allowed for. What bounds it is the poll, set to one second here, and
`MisfireHandlingMode.Relaxed`, which collapses every missed occurrence onto the instant the poll ran.

### S5 — what one schedule costs

Fifty thousand schedules into a store that started empty, through the API each library teaches, each
due an hour out so nothing fires while the measurement runs. This is `ScheduleJobBenchmark`'s
arrangement, so the Quartz rows are comparable with the numbers earlier in this file. Three sittings,
the middle one in reverse order.

| Call | Mean (3 sittings) | Allocated |
|----- |-----------------: |---------: |
| `ITimeTickerManager.AddAsync`                   |  1.06-1.28 µs |   562 B |
| `ICronTickerManager.AddAsync`                   |  1.96-2.35 µs |   450 B |
| Hangfire recurring job, `AddOrUpdate`           |  6.58-6.92 µs | 5,040-5,159 B |
| Hangfire background job, `Schedule`             |  6.54-7.08 µs | 7,240-7,416 B |
| `IScheduler.ScheduleJob`, simple trigger        |  7.67-7.93 µs | 3,540-3,656 B |
| `IScheduler.ScheduleJob`, cron trigger          |  9.84-10.09 µs | 4,430-4,537 B |

**Quartz loses this row, and it is the one that makes the rest of the table worth reading.** Writing a
schedule costs it six to eight times what it costs TickerQ, and about a third more than Hangfire.

Two things are worth saying beside it, neither of which changes the ordering. The first is that the
three calls do not store the same thing: `ScheduleJob(job, trigger)` writes a job detail *and* a
trigger, Hangfire's `Schedule` writes a job with its serialised method and arguments, and TickerQ's
`AddAsync` writes a ticker naming a function that was registered at compile time by its source
generator — there is no job to store. The second is that Quartz's number is a *schedule*, not a
*firing*: a trigger it writes once will fire for years, and S1 is what that costs. A reader choosing
between them on this row alone would be choosing on the operation Quartz does least often.

The cron row is measured on `89fbadcdc2`, which is before #3801's cron fast path landed; that work
moves `CronExpression` parsing and next-fire-time, so this row is due a re-run on top of it.

### Verdict

**In memory, Quartz starts an execution faster than either of the other two and allocates less doing
it** — 3.9-5.5 µs and 3.46 KB against TickerQ's 8.7-10.3 µs and 4.9-5.4 KB and Hangfire's
14.9-17.1 µs and 24.4 KB — **and it is an order of magnitude quicker to get a job that is wanted now
into a worker**: 58-70 µs p50 against 14.7 ms and 30.7 ms. On a one-second recurring schedule at its
defaults it is the only one of the three that put every one of six thousand firings inside fifty
milliseconds of the second it was due.

**On a database it loses to TickerQ, and not narrowly.** 10.5-11.3 ms an execution against 2.9, and
19.6-26 statements against 1.96. Some of that is the workload — a one-off schedule is Quartz's most
expensive kind, and TickerQ leaves its completed rows where they are — and some of it is a per-execution
acquisition round the defaults profile pays and the tuned one does not. Neither reading makes 19.6
statements look like a floor.

**It loses the schedule-writing row by six to eight times to TickerQ**, which writes a ticker naming
a function that was registered at compile time where Quartz writes a job detail and a trigger.

Both halves are on this page on purpose. The comparison this answers publishes only the half its
author wins.

## What #3802 D3 changed (2026-09-19, AMD Ryzen 9 5950X)

The fire path, cut one measured change at a time, in the order #3802's D1 profile ranked them.
Nothing here changes what a firing does: every listener sees what it saw, every store call happens in
the same order under the same locks, and the one public addition is a default interface member.

**Machine and runtime.** BenchmarkDotNet v0.15.8; Windows 11 (10.0.26200/25H2); AMD Ryzen 9 5950X
3.40 GHz, 1 CPU, 32 logical and 16 physical cores; .NET SDK 10.0.401; .NET 10.0.12, X64 RyuJIT
x86-64-v3, concurrent workstation GC. Base commit `637efaed4c`.

**How these were taken.** Each cut was measured against the commit before it: five alternating pairs
of `FireThroughputBenchmark` at `MaxConcurrency` 10 and 50, the order reversed between pairs, two
built trees run one after the other in one sitting. The rows below are medians over the five, and the
`Allocated` column - which is exact whatever the machine is doing - is the one to read. The box was
not quiet; other agent sessions were building and testing throughout, which is why one sitting in
five reads 30% high and why the time column is reported as medians rather than as means with errors.
The runs are in process (`--inProcess`), which for this benchmark agrees with the out-of-process
figures the D1 report took: 2.02 us and 2.57 KB at pool 10 on the base commit against D1's 1.747 us
and 2.56 KB on a quieter box.

**One caveat about `--inProcess`, learnt here.** A case whose `[GlobalSetup]` allocates far more than
its workload - `OneOffTriggerChurnBenchmark` builds twenty thousand triggers - has that setup
attributed to its operations when the run is in process, and reads 156 KB an operation where the
out-of-process run reads 424 B. Measure a setup-heavy case out of process.

### Cut by cut, at `MaxConcurrency` 10

| # | Cut | B/firing after | Δ B | Time |
|---|---|---:|---:|---|
| — | base `637efaed4c` | 2,570 | — | 2.17 us |
| 1 | an empty job data map costs nothing | 2,335 | −236 | no change |
| 2 | one task per dispatch instead of three | 1,976 | −359 | −6% |
| 4 | one ambient slot written per firing instead of two | 1,874 | −102 | no change |
| 5 | the run shell is dispatched as state, not as a closure | 1,823 | −51 | no change |
| 7 | the ungrouped execution bucket keeps its ledger entry | 1,823 | 0 | no change |
| 9 | the execution context publishes its lazies without a lock | 1,782 | −41 | no change |
| 6 | the clock is read for a span only when there is a span | 1,772 | −10 | **−75 ns** |
| — | **total** | **1,772** | **−798** | **1.61 us** |

"No change" means below this sitting's resolution, not zero: the five-pair spread on the time column
is 0.4 us at pool 10, so only a cut worth more than that shows in it. Cut 6 is the exception and is
the clearest single time reading in the series, because `JobRunShellBenchmark` resolves it directly.

### Before and after

Taken twice: against the base the cuts were written on, and again after rebasing onto
`738cf092da`, which is main with conditional continuations in it - a firing there carries the
outcome plumbing and costs about sixty bytes more, so the second pair of rows is what a reader of
this branch gets.

| | `MaxConcurrency` | Median (5 pairs) | Allocated | Fires/second |
|---|---|---:|---:|---:|
| `637efaed4c` | 10 | 2.174 us | 2.57 KB | 460,000 |
| after, on that base | 10 | 1.609 us | 1.76 KB | 622,000 |
| `637efaed4c` | 50 | 1.897 us | 2.56 KB | 527,000 |
| after, on that base | 50 | 1.326 us | 1.77 KB | 754,000 |
| `738cf092da` | 10 | 1.927 us | 2.63 KB | 519,000 |
| **after, on `738cf092da`** | 10 | **1.691 us** | **1.83 KB** | 591,000 |
| `738cf092da` | 50 | 1.803 us | 2.63 KB | 555,000 |
| **after, on `738cf092da`** | 50 | **1.544 us** | **1.84 KB** | 648,000 |

**A firing allocates 30% less either way** - 810 bytes on the second sitting, 800 on the first - and
the time column reads 12-14% better on the second and 26-30% on the first, which is the spread of
this measurement rather than a difference between the two bases. The other suites move with it:

| Suite | Before | After |
|---|---|---|
| `JobRunShellBenchmark` | 468-475 ns / 784 B | 392-404 ns / 552 B |
| `DefaultThreadPoolBenchmark.TryRun_CompletedTask_*` | 620-660 ns / 344 B | 535-560 ns / 64 B |
| `DefaultThreadPoolBenchmark.TryRun_OneShot` | 1,627-1,919 ns / 1,015 B | 1,407-1,516 ns / 731 B |
| `ScheduleJobBenchmark.ScheduleJob_SimpleTrigger` | 3.81 KB | 3.58 KB |
| `ScheduleJobBenchmark.ScheduleJob_CronTrigger` | 4.47 KB | 4.33 KB |

**The time target was met and the allocation target was not.** #3802 asked for ≤ 1.5 KB and
1.4-1.6 us; on the base the cuts were written on this is 1.76 KB and 1.61 us at pool 10, and 1.77 KB
and 1.33 us at 50, and on `738cf092da` it is 1.83 KB and 1.69 us at pool 10. The time is the
surprise: D1 predicted "treat ~1.4-1.6 us as the result of cut 12, not of cuts 1-11", and cuts 1-9
reached it without touching contention at all - because two of them, the task machinery and the
thread-pool accounting, remove thread hand-offs as well as bytes.

The allocation shortfall is 270 bytes and it is accounted for. Of the estimates D1 published, three
cuts delivered less than predicted: cut 5 delivered 51 B against 166 B (the state machine of the
lambda it removed is replaced by one inside the pool), cut 7 delivered nothing at ten concurrent
firings (the ledger entry it keeps resident was already resident at that concurrency; what it removes
is the churn of a scheduler that fires occasionally), and cut 3 was not taken. What is left, by the
D1 census: the trigger clone the store makes per acquisition (250 B), `TriggerFiredBundle` and
`TriggerFiredResult` (217 B, frozen 4.0 shapes), `JobExecutionContextImpl` itself (190 B), the DI
scope (161 B, cut 3), the job detail clone (71 B) and the per-firing `CancellationTokenSource` (44 B,
cut 10, which #3802 rules a semantic hazard). Reaching 1.5 KB means taking cut 3 and one of the
frozen shapes, and neither is a change this pass could make without changing behaviour.

### One durable job behind many triggers

`OneOffTriggerChurnBenchmark` is new, and is what #3823 turned into. One operation adds a trigger to a
durable job that already has the stated number and removes it again, so the parameter really is "how
many other triggers the job has". Out of process, `--job Short`:

| Triggers behind the job | Before | After |
|---|---:|---:|
| 2,000 | 15.31 us / 16.06 KB | **346 ns / 424 B** |
| 20,000 | 183.30 us / 156.77 KB | **434 ns / 424 B** |

The store kept a job's triggers in a list, so removing one walked it and then built an array of the
remaining keys to ask whether the job was orphaned - eight bytes per other trigger, on every
completion that deletes one. That is the shape `ScheduleJob<TJob, TInput>` produces, one durable job
per job type and a trigger per call, so it is the one-off API's steady state rather than an odd
arrangement. The index is keyed now and the orphan question is asked of the index.

`FireThroughputBenchmark` grew a `JobCount` parameter so the ordinary hundred-job schedule and the
one-job shape are both measured. Both read 1.76-1.79 KB a firing at either pool size, before the fix
and after it - that row is the guard rather than the fix, because the fire-throughput workload's
triggers repeat forever and are never removed, so it never reaches the path #3823 is about.

### What was measured and rejected

- **Cut 3, skipping the dependency-injection scope**, was designed and not taken. It is 161 B, the
  largest single cut left, and it is the only one of the eleven with behaviour a deployment can
  observe: `ConfigureScope` is a documented hook and a derived factory may override it, so eliding
  the scope has to be gated on the factory being exactly `MicrosoftDependencyInjectionJobFactory`
  with no `ConfigureScope` delegate, and on the job type being one the container would build without
  resolving anything. A job that keeps its scope keeps every byte of it, and the arithmetic above
  says the cut does not reach 1.5 KB on its own either. Worth doing; worth doing on its own.
- **Cut 10, reusing the `CancellationTokenSource`**, is out of scope by #3802's own ruling: a leaked
  token could observe another firing's interrupt.
- **Cut 11a, the spurious `SortedSet.Remove` in `TriggersFired`**, was traced and not taken. The
  removal is reached only with `tw.state == Acquired`, and every site that adds to `timeTriggers`
  either sets a different state first or runs before the state is set - so the removal really does
  always miss. It allocates nothing, saves a tree walk of a few tens of nanoseconds, and rests on an
  invariant that nothing in the suite pins; a change that silently corrupts the store if the
  invariant ever stops holding is not worth that.
- **A heap replacing the `SortedSet`** stays unsettled, exactly as D1 left it. Nothing here moved the
  comparator's share and nothing here measured it.
- **Cut 12, contention**, was not attempted. D1's finding stands: at pool 10 the fire path spends
  more thread time blocked on the store's monitor than it spends on CPU. Cuts 2 and 5 remove two
  thread hand-offs per firing, which is part of why the time target was met, but the store's lock is
  untouched.
