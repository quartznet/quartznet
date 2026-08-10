using Quartz.Listeners;

using Serilog;

namespace Quartz.Examples.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<Worker>();

                // if you are using persistent job store, you might want to alter some options
                services.Configure<QuartzOptions>(options =>
                {
                    options.Scheduling.IgnoreDuplicates = true; // default: false
                    options.Scheduling.OverwriteExistingData = true; // default: true
                });

                // base configuration for DI
                services.AddQuartz(q =>
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

                    q.AddTrigger<IJob>(t => t
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
                services.AddQuartzHostedService(options =>
                {
                    // when shutting down we want jobs to complete gracefully
                    options.WaitForJobsToComplete = true;

                    // when we need to init another IHostedServices first
                    options.StartDelay = TimeSpan.FromSeconds(10);
                });
            });
}

public class TestSchedulerListener : ISchedulerListener
{
    private readonly ILogger<TestSchedulerListener> logger;

    public TestSchedulerListener(ILogger<TestSchedulerListener> logger)
    {
        this.logger = logger;
    }

    public ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Scheduler starting");
        return ValueTask.CompletedTask;
    }
}

public class TestJobListener : IJobListener
{
    private readonly ILogger<TestJobListener> logger;

    public TestJobListener(ILogger<TestJobListener> logger)
    {
        this.logger = logger;
    }

    public string Name => nameof(TestJobListener);

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Job {Job} to be executed", context.JobDetail.Key);
        return ValueTask.CompletedTask;
    }
}

public class TestTriggerListener : ITriggerListener
{
    private readonly ILogger<TestTriggerListener> logger;

    public TestTriggerListener(ILogger<TestTriggerListener> logger)
    {
        this.logger = logger;
    }

    public string Name => nameof(TestSchedulerListener);

    public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Trigger {Trigger} fired", trigger.Key);
        return ValueTask.CompletedTask;
    }
}