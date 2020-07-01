using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Simpl;

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

            services.AddQuartzMicrosoftLoggingBridge();

            services.AddQuartz(quartz => quartz
                .WithMicrosoftDependencyInjectionJobFactory()
                .WithTypeLoadHelper<SimpleTypeLoadHelper>()
                .UseInMemoryStore()
                .WithDefaultThreadPool(threadPool => threadPool.WithThreadCount(10))
                .WithId("Scheduler-Core")
                .WithName("Quartz ASP.NET Core Sample Scheduler")
            );
            var jobKey = new JobKey("job", "group");
            services.AddQuartzJob<ExampleJob>(configure => configure
                .WithIdentity(jobKey)
                .WithDescription("my awesome job")
            );
            services.AddQuartzTrigger(configure => configure
                .ForJob(jobKey)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
                .WithDescription("my awesome trigger")
            );
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