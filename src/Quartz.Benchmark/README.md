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
  calls `CronExpression.ValidateExpression(cronExpression)` — whose whole body is
  `var _ = new CronExpression(cronExpression);` — and then hands the same string to
  `CronScheduleNoParseException`, which constructs a second one. Two parses at 307 ns and 576 B each
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
