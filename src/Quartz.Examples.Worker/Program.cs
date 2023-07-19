using Serilog;

namespace Quartz.Examples.Worker
{
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
                            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(7)))
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

                    });

                    // Quartz.Extensions.Hosting hosting
                    services.AddQuartzHostedService(options =>
                    {
                        // when shutting down we want jobs to complete gracefully
                        options.WaitForJobsToComplete = true;

                        // when we need to init another IHostedServices first
                        options.StartDelay = TimeSpan.FromSeconds(10);
                    });
                });
    }
}
