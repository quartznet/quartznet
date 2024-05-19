using Quartz.Listener;

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
                    options.Scheduling.OverWriteExistingData = true; // default: true
                });

                // base configuration for DI
                services.AddQuartz(q =>
                {
                    // handy when part of cluster or you want to otherwise identify multiple schedulers
                    q.SchedulerId = "Scheduler-Core";

                    var loggerFactory = new LoggerFactory()
                        .AddSerilog(Log.Logger);
                    q.SetLoggerFactory(loggerFactory);

                    // whether we want to validate used configuration properties, defaults to true
                    q.CheckConfiguration = true;

                    // we take this from appsettings.json, just show it's possible
                    // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

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
                        .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow))
                        .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
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

                // Quartz.Extensions.Hosting hosting
                services.AddQuartzHostedService(options =>
                {
                    // when shutting down we want jobs to complete gracefully
                    options.WaitForJobsToComplete = true;

                    // when we need to init another IHostedServices first
                    options.StartDelay = TimeSpan.FromSeconds(10);

                    options.HostedServiceStartingHandler = HostedServiceStartingHandler;
                    options.HostedServiceStartedHandler = HostedServiceStartedHandler;
                    options.HostedServiceStoppingHandler = HostedServiceStoppingHandler;
                    options.HostedServiceStoppedHandler = HostedServiceStoppedHandler;

                    static async Task HostedServiceStartingHandler(IServiceProvider provider, CancellationToken cancellationToken)
                    {
                        provider.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(nameof(HostedServiceStartingHandler))
                            .LogInformation("The {0} executes with a delay!!!", nameof(HostedServiceStartingHandler));

                        await Task.Delay(25, cancellationToken);
                    }

                    static async Task HostedServiceStartedHandler(IServiceProvider provider, CancellationToken cancellationToken)
                    {
                        provider.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(nameof(HostedServiceStartedHandler))
                            .LogInformation("The {0} executes with a delay!!!", nameof(HostedServiceStartedHandler));

                        await Task.Delay(25, cancellationToken);
                    }

                    static async Task HostedServiceStoppingHandler(IServiceProvider provider, CancellationToken cancellationToken)
                    {
                        provider.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(nameof(HostedServiceStoppingHandler))
                            .LogInformation("The {0} executes with a delay!!!", nameof(HostedServiceStoppingHandler));

                        await Task.Delay(25, cancellationToken);
                    }

                    static async Task HostedServiceStoppedHandler(IServiceProvider provider, CancellationToken cancellationToken)
                    {
                        provider.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(nameof(HostedServiceStoppedHandler))
                            .LogInformation("The {0} executes with a delay!!!", nameof(HostedServiceStoppedHandler));

                        await Task.Delay(25, cancellationToken);
                    }
                });
            });
}

public class TestSchedulerListener : SchedulerListenerSupport
{
    private readonly ILogger<TestSchedulerListener> logger;

    public TestSchedulerListener(ILogger<TestSchedulerListener> logger)
    {
        this.logger = logger;
    }

    public override ValueTask SchedulerStarting(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Scheduler starting");
        return ValueTask.CompletedTask;
    }
}

public class TestJobListener : JobListenerSupport
{
    private readonly ILogger<TestJobListener> logger;

    public TestJobListener(ILogger<TestJobListener> logger)
    {
        this.logger = logger;
    }

    public override string Name => nameof(TestJobListener);

    public override ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Job {Job} to be executed", context.JobDetail.Key);
        return ValueTask.CompletedTask;
    }
}

public class TestTriggerListener : TriggerListenerSupport
{
    private readonly ILogger<TestTriggerListener> logger;

    public TestTriggerListener(ILogger<TestTriggerListener> logger)
    {
        this.logger = logger;
    }

    public override string Name => nameof(TestSchedulerListener);

    public override ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Trigger {Trigger} fired", trigger.Key);
        return ValueTask.CompletedTask;
    }
}