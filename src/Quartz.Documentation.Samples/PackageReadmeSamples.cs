using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Jobs;

namespace Quartz.Documentation.Samples;

/// <summary>
/// The code the per-package readmes show — <c>src/&lt;Package&gt;/README.md</c>, which is what nuget.org
/// renders on a package page.
/// </summary>
/// <remarks>
/// Deliberately separate from the samples under <c>Packages</c>, which belong to the documentation pages:
/// a readme is a shop window and wants the shortest thing that works, where a page wants the whole story.
/// Both are compiled here, so neither can outlive the API it names — and a broken sample on nuget.org is
/// the first thing a new user meets.
/// </remarks>
public static class PackageReadmeSamples
{
    public static class Core
    {
        #region sample_readme_quartz_job

        public sealed class HelloJob : IJob
        {
            public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
            {
                await Console.Out.WriteLineAsync("Greetings from HelloJob!");
            }
        }

        #endregion

        public static void UnderAHost(string[] args)
        {
            #region sample_readme_quartz_host

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.AddQuartz(q => q.ScheduleJob<HelloJob>(trigger => trigger
                .WithIdentity("hello")
                .WithSimpleSchedule(TimeSpan.FromSeconds(10))));

            builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

            #endregion
        }

        public static async ValueTask WithoutAHost()
        {
            #region sample_readme_quartz_standalone

            IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();
            await scheduler.Start();

            #endregion
        }
    }

    public static class AspNetCore
    {
        public static void Registration(string[] args)
        {
            #region sample_readme_aspnetcore

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.AddQuartz();
            builder.Services.AddQuartzHttpApi();
            builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
            builder.Services.AddHealthChecks().AddQuartz();

            WebApplication app = builder.Build();

            app.MapQuartzHttpApi().RequireAuthorization();
            app.MapHealthChecks("/healthz");

            #endregion
        }
    }

    public static class Dashboard
    {
        public static void Registration(string[] args)
        {
            #region sample_readme_dashboard

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.AddQuartz();
            builder.Services.AddQuartzHttpApi();
            builder.Services.AddQuartzDashboard();
            builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

            WebApplication app = builder.Build();

            app.UseAntiforgery();
            app.MapQuartzHttpApi();
            app.MapQuartzDashboard();

            #endregion
        }
    }

    public static class Redis
    {
        public static void LockHandler(IHostApplicationBuilder builder, string connectionString)
        {
            #region sample_readme_redis

            builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.UseClustering();

                // job and trigger data stays in the database; only the locks move to Redis
                store.UseRedisLockHandler(redis => redis.RedisConfiguration = "redis-server:6379");
            }));

            #endregion
        }
    }

    public static class HttpClient
    {
        public static void Registration(IHostApplicationBuilder builder)
        {
            #region sample_readme_httpclient

            builder.Services.AddHttpClient("quartz", client =>
                client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/"));

            builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");

            #endregion
        }
    }

    public static class Jobs
    {
        public static void SendMail(IHostApplicationBuilder builder)
        {
            #region sample_readme_jobs

            builder.Services.AddQuartz(q => q.ScheduleJob<SendMailJob>(
                trigger => trigger.WithIdentity("nightlyDigest").WithCronSchedule("0 0 6 * * ?"),
                job => job.UsingSendMailOptions(new SendMailOptions
                {
                    SmtpHost = "smtp.example.com",
                    Sender = "scheduler@example.com",
                    Recipient = "ops@example.com",
                    Subject = "Nightly digest",
                    Message = "Everything ran.",
                })));

            #endregion
        }
    }

    public static class Plugins
    {
        public static void Registration(IHostApplicationBuilder builder)
        {
            #region sample_readme_plugins

            builder.Services.AddQuartz(q =>
            {
                q.UseStructuredJobLogging();
                q.UseStructuredTriggerLogging();
                q.UseJsonSchedulingConfiguration(x => x.Files.Add("quartz_jobs.json"));
            });

            #endregion
        }
    }

    public static class TimeZoneConverter
    {
        public static void Registration(IHostApplicationBuilder builder)
        {
            #region sample_readme_timezoneconverter

            builder.Services.AddQuartz(q => q.UseTimeZoneConverter());

            #endregion
        }
    }

    public static class Newtonsoft
    {
        public static void Registration(IHostApplicationBuilder builder, string connectionString)
        {
            #region sample_readme_newtonsoft

            builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
                store.UseNewtonsoftJsonSerializer();
            }));

            #endregion
        }
    }
}
