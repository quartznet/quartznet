using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            // base configuration for DI
            services.AddQuartz(q =>
            {
                // hooks LibLog to Microsoft logging without allowing it to detect concrete implementation
                q.UseQuartzMicrosoftLoggingBridge();

                q.UseMicrosoftDependencyInjectionJobFactory();
                q.UseSimpleTypeLoader();
                q.UseInMemoryStore();
                q.UseDefaultThreadPool(tp => tp.SetThreadCount(10));
                
                q
                    .SetSchedulerId("Scheduler-Core")
                    .SetSchedulerName("Quartz ASP.NET Core Sample Scheduler");
                
                var jobKey = new JobKey("awesome job", "awesome group");
                q.AddJob<ExampleJob>(j => j
                    .StoreDurably()
                    .WithIdentity(jobKey)
                    .WithDescription("my awesome job")
                );

                q.AddTrigger(t => t
                    .ForJob(jobKey)
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
                    .WithDescription("my awesome simple trigger")
                );

                q.AddTrigger(t => t
                    .ForJob(jobKey)
                    .WithCronSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 10))
                    .WithDescription("my awesome cron trigger")
                );

                q.AddTrigger(t => t
                    .ForJob(jobKey)
                    .WithDailyTimeIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Second))
                    .WithDescription("my awesome daily time interval trigger")
                );
            });

            // ASP.NET Core hosting
            services.AddQuartzServer();

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