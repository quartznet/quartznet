using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                    
                    // base configuration for DI
                    services.AddQuartz(q =>
                    {
                        // handy when part of cluster or you want to otherwise identify multiple schedulers
                        q.SchedulerId = "Scheduler-Core";
                        
                        // we take this from appsettings.json, just show it's possible
                        // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

                        // this is default configuration if you don't alter it
                        q.UseMicrosoftDependencyInjectionJobFactory(options =>
                        {
                            // if we don't have the job in DI, allow fallback to configure via default constructor
                            options.AllowDefaultConstructor = true;
                    
                            // set to true if you want to inject scoped services like Entity Framework's DbContext
                            options.CreateScope = false;
                        });

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
                    });
                });
    }
}
