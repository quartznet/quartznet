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

    // configure jobs with code
    var jobKey = new JobKey("awesome job", "awesome group");
    q.AddJob<ExampleJob>(j => j
        .StoreDurably()
        .WithIdentity(jobKey)
        .WithDescription("my awesome job")
    );

    q.AddTrigger(t => t
        .WithIdentity("Simple Trigger")
        .ForJob(jobKey)
        .StartNow()
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
        .WithDescription("my awesome simple trigger")
    );

    q.AddTriggerListener<TestTriggerListener>();
    q.AddJobListener<TestJobListener>();
    q.AddSchedulerListener<TestSchedulerListener>();
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
