# The 3.x baseline for the fire-throughput numbers

`FireThroughputBenchmark` and `FireThroughputPostgresBenchmark` are only a comparison if the number
they are compared against was taken the same way. `FireThroughputBaselineBenchmark.cs` in this folder
is that other half: the same workload and the same settings, written against 3.x's API.

**Nothing here is compiled.** `Quartz.Benchmark.csproj` excludes the folder. It is here so that the
published comparison can be re-run rather than taken on trust, and so that the next person to move
these numbers does not have to reconstruct the baseline from the prose.

**Nothing is committed on `3.x`.** The baseline is a measurement of the released shape of 3.x, not a
change to it.

## Running it

```shell
# A checkout of 3.x, anywhere. A worktree of this repository is the easiest one.
git worktree add ../quartznet-3x origin/3.x

cp src/Quartz.Benchmark/baseline-3x/FireThroughputBaselineBenchmark.cs \
   ../quartznet-3x/src/Quartz.Benchmark/
```

`3.x`'s benchmark project needs two references it does not carry, because nothing in it has ever
touched a database:

```xml
<PackageReference Include="Npgsql" Version="8.0.7" />
<ProjectReference Include="..\Quartz.Serialization.SystemTextJson\Quartz.Serialization.SystemTextJson.csproj" />
```

`Npgsql` is the driver `quartz.dataSource.default.provider = Npgsql` resolves, and the serializer
project is what `quartz.serializer.type = stj` resolves; the version is the one `3.x`'s own
integration tests pin, so the baseline is measured against the driver that branch is tested with.

Then, against the same PostgreSQL container the 4.x run used — it already has the schema, which is
why this harness does not apply one:

```shell
docker run -d --name quartz-bench-pg -p 55432:5432 \
  -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet postgres:15.1

$env:QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'

dotnet run -c Release --project src/Quartz.Benchmark -- --filter '*FireThroughput*'
```

## What is held identical, and what is not

Held identical, because a difference in any of them would be measured as a difference in the
scheduler: two thousand triggers over a hundred jobs; simple triggers repeating indefinitely at a
one-millisecond interval under the ignore-misfires instruction, so every trigger is permanently
overdue and the acquisition loop never waits; `MaxBatchSize` equal to `MaxConcurrency`; a
one-second batch fire-ahead window; a one-second idle wait; a job that counts and returns; and the
same fires-per-invocation constants, so `Mean` is the time one firing took on both sides.

A millisecond rather than anything shorter because that is the smallest interval a persistent
store can carry on either branch: `StdAdoDelegate.GetDbTimeSpanValue` writes a `TimeSpan` as whole
milliseconds, and a simple trigger read back with a zero repeat interval throws
`DivideByZeroException` out of `GetFireTimeAfter` on its next firing. Two thousand triggers rather
than two hundred because the count sets the arrangement's own ceiling — a trigger repeating every
millisecond sustains a thousand firings a second, so two thousand of them put the limit several
times above what the fastest arm reaches.

Different, because 3.x is 3.x: the store is `JobStoreTX` rather than `LocalTransactionJobStore` — the
same store under its 3.x name; the scheduler is built from flat properties through
`StdSchedulerFactory`, which is 3.x's configuration surface; and `IJob.Execute` returns `Task` and
takes no cancellation token. None of the three is on the fire path being measured.
