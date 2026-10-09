# Quartz.Dashboard.Agent

[Quartz.Dashboard.Agent](https://www.nuget.org/packages/Quartz.Dashboard.Agent) puts a scheduler on a
[Quartz dashboard](https://www.nuget.org/packages/Quartz.Dashboard) running somewhere else, over one
outbound connection the scheduler opens itself. The dashboard shows and drives it — every page, every
action, the history and the live events — without an inbound port on the worker, a plugin that serves
HTTP, or a shared database.

## Installation

```shell
dotnet add package Quartz.Dashboard.Agent
```

The dashboard has to accept agents: `AddQuartzDashboard(options => options.AcceptAgents(…))` in the
dashboard's application, which maps the agent hub at `{DashboardPath}/agents`.

## Usage

<!-- snippet: sample_readme_dashboard_agent -->
```csharp
builder.AddQuartz(q =>
{
    q.UseDashboardAgent(agent =>
    {
        agent.Endpoint = new Uri("https://ops.example.com/quartz/agents");
        agent.Token = builder.Configuration["Dashboard:AgentToken"];
        agent.Target = "worker-1";

        // Refused until said: an agent crosses a trust boundary, and a job type is a string
        // the dashboard sends.
        agent.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal);
    });
});
```
<!-- endSnippet -->

`Target` is the first half of the scheduler's key on the dashboard, `w1/QuartzScheduler`; it defaults to
the machine name. The token goes as an `Authorization: Bearer` header, never in the URL. The agent
reconnects for as long as the scheduler runs, and a dashboard that is down when the worker starts is
dialled again until it answers.

The agent refuses every job type until `IsJobTypeAllowed` says which it accepts, because the connection
crosses a trust boundary and a job type is a string the dashboard sends. `ReadOnly` refuses every
mutation and `IsOperationAllowed` refuses by route name; all three are decided in the worker's process,
and nothing the dashboard sends can widen them.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/dashboard-agent.html>
