# One-Off Job

A job that runs once.

:::tip

Misfire Mode: Smart

:::

## Ahead of Time Job Registration

Register a static set of jobs ahead of time. With `durable` set to `true`, the job stays dormant until it is triggered.

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity("name", "group")
                        .Build();
    
    var replace = true;
    var durable = true;
    await scheduler.AddJob(job, replace, durable, ct);
}
```

Trigger it later with `TriggerJob`:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    await scheduler.TriggerJob(new JobKey("name", "group"), ct);
}
```

With a `JobDataMap`:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var jobData = new JobDataMap();
    await scheduler.TriggerJob(new JobKey("name", "group"), jobData, ct);
}
```

## Dynamic Registration

For a dynamic set of jobs, create the job and trigger on the fly:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity("name", "group")
                        .Build();

    var trigger = TriggerBuilder.Create()
        .WithIdentity("name", "group")
        .StartNow()
        .Build();

    await scheduler.ScheduleJob(job, trigger, ct);
}
```

The above is the same as:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var job = JobBuilder.Create<AnExampleJob>()
                        .WithIdentity("name", "group")
                        .Build();

    var trigger = TriggerBuilder.Create()
        .WithIdentity("name", "group")
        .WithSimpleSchedule()
        .StartNow()
        .Build();

    await scheduler.ScheduleJob(job, trigger, ct);
}
```
