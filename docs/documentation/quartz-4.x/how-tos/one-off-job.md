# One-Off Job

You can run the simplest job this way.

:::tip

Misfire Mode: Smart

:::

## Ahead of Time Job Registration

If you have a static set of jobs, you can register them ahead of time using something like this.
If the `durable` flag is `true`, then the job will stay dormant until its triggered.

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

To trigger the job later, simply call `TriggerJob` like below:

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    await scheduler.TriggerJob(new JobKey("name", "group"), ct);
}
```

If you want to adjust the `JobDataMap`

```csharp
public async Task DoSomething(IScheduler scheduler, CancellationToken ct)
{
    var jobData = new JobDataMap();
    await scheduler.TriggerJob(new JobKey("name", "group"), jobData, ct);
}
```

## Dynamic Registration

In this scenario, you may have a dynamic set of jobs and need to
generate both the job and trigger on the fly.

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
