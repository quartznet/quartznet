using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;

namespace Quartz.Examples.AspNetCore
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();

            // easier to see behavior with timestamps
            services.AddLogging(opt =>
            {
                opt.AddConsole(c =>
                {
                    c.TimestampFormat = "[HH:mm:ss] ";
                });
            });
            
            // base configuration for DI
            services.AddQuartz(q =>
            {
                // handy when part of cluster or you want to otherwise identify multiple schedulers
                q.SchedulerId = "Scheduler-Core";
                
                // we take this from appsettings.json, just show it's possible
                // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";
                
                // hooks LibLog to Microsoft logging without allowing it to detect concrete implementation
                // if you are using NLog, SeriLog or log4net you shouldn't need this
                q.UseQuartzMicrosoftLoggingBridge();

                // we could leave DI configuration intact and then jobs need to have public no-arg constructor
                
                // the MS DI is expected to produce transient job instances 
                q.UseMicrosoftDependencyInjectionJobFactory(options =>
                {
                    // if we don't have the job in DI, allow fallback to configure via default constructor
                    options.AllowDefaultConstructor = true;
                });

                // or 
                // q.UseMicrosoftDependencyInjectionScopedJobFactory();
                
                // these are the defaults
                q.UseSimpleTypeLoader();
                q.UseInMemoryStore();
                q.UseDefaultThreadPool(tp =>
                {
                    tp.ThreadCount = 10;
                });
                
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

                q.AddTrigger(t => t
                    .WithIdentity("Cron Trigger")    
                    .ForJob(jobKey)
                    .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(3)))
                    .WithCronSchedule("0/3 * * * * ?")
                    .WithDescription("my awesome cron trigger")
                );

                q.AddTrigger(t => t
                    .WithIdentity("Daily Trigger")    
                    .ForJob(jobKey)
                    .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(5)))
                    .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                    .WithDescription("my awesome daily time interval trigger")
                );
                
                // also add XML configuration and poll it for changes
                q.UseXmlSchedulingConfiguration(x =>
                {
                    x.Files = new[] { "~/quartz_jobs.config" };
                    x.ScanInterval = TimeSpan.FromSeconds(2);
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
                endpoints.MapHealthChecksUI();
            });
        }
    }
}