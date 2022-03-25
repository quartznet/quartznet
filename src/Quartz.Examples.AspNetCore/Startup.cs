using System;
using System.Globalization;

using HealthChecks.UI.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenTelemetry.Trace;

using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Plugin.Interrupt;

using Serilog;

namespace Quartz.Examples.AspNetCore
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // make sure you configure logging and open telemetry before quartz services

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(dispose: true);
            });

            services.AddOpenTelemetryTracing(builder =>
            {
                builder
                    .AddQuartzInstrumentation()
                    .AddZipkinExporter(o =>
                    {
                        o.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
                    })
                    .AddJaegerExporter(o =>
                    {
                        // these are the defaults
                        o.AgentHost = "localhost";
                        o.AgentPort = 6831;
                    });
            });

            services.AddRazorPages();

            // base configuration for DI, read from appSettings.json
            services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));

            // if you are using persistent job store, you might want to alter some options
            services.Configure<QuartzOptions>(options =>
            {
                options.Scheduling.IgnoreDuplicates = true; // default: false
                options.Scheduling.OverWriteExistingData = true; // default: true
            });

            services.AddQuartz(q =>
            {
                // handy when part of cluster or you want to otherwise identify multiple schedulers
                q.SchedulerId = "Scheduler-Core";

                // you can control whether job interruption happens for running jobs when scheduler is shutting down
                q.InterruptJobsOnShutdown = true;

                // when QuartzHostedServiceOptions.WaitForJobsToComplete = true or scheduler.Shutdown(waitForJobsToComplete: true)
                q.InterruptJobsOnShutdownWithWait = true;

                // we can change from the default of 1
                q.MaxBatchSize = 5;

                // we take this from appsettings.json, just show it's possible
                // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

                // this is default configuration if you don't alter it
                q.UseMicrosoftDependencyInjectionJobFactory();

                // these are the defaults
                q.UseSimpleTypeLoader();
                q.UseInMemoryStore();
                q.UseDefaultThreadPool(maxConcurrency: 10);

                // quickest way to create a job with single trigger is to use ScheduleJob
                q.ScheduleJob<ExampleJob>(trigger => trigger
                    .WithIdentity("Combined Configuration Trigger")
                    .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(7)))
                    .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                    .WithDescription("my awesome trigger configured for a job with single call")
                );

                // you can also configure individual jobs and triggers with code
                // this allows you to associated multiple triggers with same job
                // (if you want to have different job data map per trigger for example)
                q.AddJob<ExampleJob>(j => j
                    .StoreDurably() // we need to store durably if no trigger is associated
                    .WithDescription("my awesome job")
                );

                // here's a known job for triggers
                var jobKey = new JobKey("awesome job", "awesome group");
                q.AddJob<ExampleJob>(jobKey, j => j
                    .WithDescription("my awesome job")
                );

                q.AddTrigger(t => t
                    .WithIdentity("Simple Trigger")
                    .ForJob(jobKey)
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
                    .WithDescription("my awesome simple trigger")
                );

                q.AddTrigger(t => t
                    .WithIdentity("Cron Trigger")
                    .ForJob(jobKey)
                    .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(3)))
                    .WithCronSchedule("0/3 * * * * ?")
                    .WithDescription("my awesome cron trigger")
                );

                // auto-interrupt long-running job
                q.UseJobAutoInterrupt(options =>
                {
                    // this is the default
                    options.DefaultMaxRunTime = TimeSpan.FromMinutes(5);
                });
                q.ScheduleJob<SlowJob>(
                    triggerConfigurator => triggerConfigurator
                        .WithIdentity("slowJobTrigger")
                        .StartNow()
                        .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever()),
                    jobConfigurator => jobConfigurator
                        .WithIdentity("slowJob")
                        .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
                        // allow only five seconds for this job, overriding default configuration
                        .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString(CultureInfo.InvariantCulture)));

                const string calendarName = "myHolidayCalendar";
                q.AddCalendar<HolidayCalendar>(
                    name: calendarName,
                    replace: true,
                    updateTriggers: true,
                    x => x.AddExcludedDate(new DateTime(2020, 5, 15))
                );

                q.AddTrigger(t => t
                    .WithIdentity("Daily Trigger")
                    .ForJob(jobKey)
                    .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(5)))
                    .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                    .WithDescription("my awesome daily time interval trigger")
                    .ModifiedByCalendar(calendarName)
                );

                // also add XML configuration and poll it for changes
                q.UseXmlSchedulingConfiguration(x =>
                {
                    x.Files = new[] { "~/quartz_jobs.config" };
                    x.ScanInterval = TimeSpan.FromMinutes(1);
                    x.FailOnFileNotFound = true;
                    x.FailOnSchedulingError = true;
                });

                // convert time zones using converter that can handle Windows/Linux differences
                q.UseTimeZoneConverter();

                // add some listeners
                q.AddSchedulerListener<SampleSchedulerListener>();
                q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals(jobKey.Group));
                q.AddTriggerListener<SampleTriggerListener>();

                // example of persistent job store using JSON serializer as an example
                /*
                q.UsePersistentStore(s =>
                {
                    s.UseProperties = true;
                    s.RetryInterval = TimeSpan.FromSeconds(15);
                    s.UseSqlServer(sqlServer =>
                    {
                        sqlServer.ConnectionString = "some connection string";
                        // this is the default
                        sqlServer.TablePrefix = "QRTZ_";
                    });
                    s.UseJsonSerializer();
                    s.UseClustering(c =>
                    {
                        c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                        c.CheckinInterval = TimeSpan.FromSeconds(10);
                    });
                });
                */
            });

            // we can use options pattern to support hooking your own configuration with Quartz's
            // because we don't use service registration api, we need to manually ensure the job is present in DI
            services.AddTransient<ExampleJob>();

            // if there is no need to use key matchers, job and trigger listeners can be added to services and Quartz will automatically use these
            services.AddSingleton<IJobListener, SecondSampleJobListener>();
            services.AddSingleton<ITriggerListener>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILogger<SecondSampleTriggerListener>>();
                return new SecondSampleTriggerListener(logger, "Example value");
            });

            services.Configure<SampleOptions>(Configuration.GetSection("Sample"));
            services.AddOptions<QuartzOptions>()
                .Configure<IOptions<SampleOptions>>((options, dep) =>
                {
                    if (!string.IsNullOrWhiteSpace(dep.Value.CronSchedule))
                    {
                        var jobKey = new JobKey("options-custom-job", "custom");
                        options.AddJob<ExampleJob>(j => j.WithIdentity(jobKey));
                        options.AddTrigger(trigger => trigger
                            .WithIdentity("options-custom-trigger", "custom")
                            .ForJob(jobKey)
                            .WithCronSchedule(dep.Value.CronSchedule));
                    }
                });


            // ASP.NET Core hosting
            services.AddQuartzServer(options =>
            {
                // when shutting down we want jobs to complete gracefully
                options.WaitForJobsToComplete = true;
            });

            services
                .AddHealthChecksUI()
                .AddInMemoryStorage();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStaticFiles();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapHealthChecks("healthz", new HealthCheckOptions
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                });
                endpoints.MapHealthChecksUI();
            });
        }
    }
}