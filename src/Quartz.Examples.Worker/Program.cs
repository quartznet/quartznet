using Quartz;
using Quartz.Examples.Worker;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

builder.Services.AddHostedService<Worker>();

// if you are using persistent job store, you might want to alter some options
builder.Services.Configure<QuartzOptions>(options =>
{
    // pass over a declared job or trigger whose key is already stored, rather than replacing it.
    // OverwriteExistingData defaults to true, and setting this turns that default off — writing both
    // down would be asking for opposite things and is refused at startup.
    options.Scheduling.IgnoreDuplicates = true; // default: false
});

// base configuration for DI. builder.AddQuartz reads the "Quartz" configuration section as well, so
// anything in appsettings.json is applied before the callback below.
builder.AddQuartz(q =>
{
    // handy when part of cluster or you want to otherwise identify multiple schedulers
    q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");

    // these are the defaults
    q.UseSimpleTypeLoader();
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 10;
    });

    // quickest way to create a job with single trigger is to use ScheduleJob
    q.ScheduleJob<ExampleJob>(trigger => trigger
        .WithIdentity("Combined Configuration Trigger")
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
        .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
        .WithDescription("my awesome trigger configured for a job with single call")
    );

    // ExampleJob carries [QuartzJob] and [CronTrigger], so the job and its schedule are declared on
    // the class rather than here. This one call adds every job this assembly declares that way; the
    // registration it runs is generated at build time and is in obj/ to read.
    q.AddDeclaredJobs();

    q.AddTriggerListener<TestTriggerListener>();
    q.AddJobListener<TestJobListener>();
    q.AddSchedulerListener<TestSchedulerListener>();

    // Put this scheduler on a dashboard running elsewhere, over one outbound connection the worker
    // opens itself. Off until Dashboard:AgentEndpoint is set — Quartz.Examples.AspNetCore accepts
    // agents, so against it:
    //   dotnet run --project src/Quartz.Examples.Worker -- --Dashboard:AgentEndpoint=http://localhost:5000/quartz/agents
    if (builder.Configuration["Dashboard:AgentEndpoint"] is { Length: > 0 } agentEndpoint)
    {
        q.UseDashboardAgent(agent =>
        {
            agent.Endpoint = new Uri(agentEndpoint);
            agent.Token = builder.Configuration["Dashboard:AgentToken"];

            // The first half of this scheduler's key on the dashboard; the machine name when unset.
            agent.Target = builder.Configuration["Dashboard:AgentTarget"];

            // An agent refuses every job type until told which it accepts: the dashboard is on the
            // other side of a trust boundary, and a job type is a string it sends.
            agent.IsJobTypeAllowed = name => name.StartsWith("Quartz.Examples.Worker.", StringComparison.Ordinal);
        });
    }
});

// run the scheduler as an IHostedService
builder.AddQuartzHostedService(options =>
{
    // when shutting down we want jobs to complete gracefully
    options.WaitForJobsToComplete = true;

    // when we need to init another IHostedServices first
    options.StartDelay = TimeSpan.FromSeconds(10);
});

builder.Build().Run();
