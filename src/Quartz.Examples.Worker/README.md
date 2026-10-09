# Quartz.NET in a worker service

The smallest realistic hosted scheduler: a `Microsoft.NET.Sdk.Worker` project that registers Quartz
with `builder.AddQuartz(...)`, runs it as an `IHostedService`, and logs through Serilog. It is what
[Hosted Services Integration](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/hosted-services-integration.html)
describes, as a project you can run.

## Running it

```shell
dotnet run --project src/Quartz.Examples.Worker
```

Nothing external is needed — the store is in memory. Note the ten-second `StartDelay` in `Program.cs`:
the first job fires about ten seconds after the host starts, which is deliberate, since the point of
that setting is to let another `IHostedService` initialize first.

## What it shows

| File | What it shows |
|---|---|
| `Program.cs` | `AddQuartz` and `AddQuartzHostedService` on `HostApplicationBuilder`, the thread pool, the simple type loader, and both ways of describing a schedule |
| `ExampleJob.cs` | An ordinary job, resolved from the container for every fire |
| `Listeners.cs` | A job, trigger and scheduler listener, registered with `AddJobListener` / `AddTriggerListener` / `AddSchedulerListener` |
| `Worker.cs` | A second hosted service beside the scheduler, which is why `StartDelay` is set |
| `appsettings.json` | The `Quartz` configuration section, applied before the `AddQuartz` callback runs |

`Program.cs` shows both shapes of schedule side by side: `ScheduleJob<T>` for one job with one trigger,
and `AddJob` plus `AddTrigger` for a job whose triggers are declared separately.

## Putting it on a dashboard

The worker can dial the dashboard `Quartz.Examples.AspNetCore` serves, with
[Quartz.Dashboard.Agent](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard-agent.html).
Start that example, then:

```shell
dotnet run --project src/Quartz.Examples.Worker -- --Dashboard:AgentEndpoint=http://localhost:5000/quartz/agents
```

The scheduler appears on `http://localhost:5000/quartz/schedulers` as `<machine name>/Quartz Worker Service Sample Scheduler`,
reached through an agent. Start a second worker with `--Dashboard:AgentTarget=w2` for a second row. The token
both sides use is `Dashboard:AgentToken` in each `appsettings.json`.

## It is also the trim canary

This project is what `dotnet fallout PublishTrimmed` publishes with `PublishTrimmed` and
`TrimMode=full`, so every `IL2xxx` the publish reports is Quartz's own — or the dashboard agent's, since
4.5. Nothing is suppressed by warning code; what is suppressed is the recorded set of types in
`src/Quartz/ILLink.Suppressions.xml` and `src/Quartz.Dashboard.Agent/ILLink.Suppressions.xml`, and a
type outside those sets warning is a failing publish. The csproj says the rest of it. Adding a package
reference here changes what that leg measures, so weigh it before you do.
