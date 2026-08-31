using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/quick-start.md.
/// </summary>
/// <remarks>
/// The three whole-program listings on that page stay hand-written fences: they are top-level
/// statements with their <c>using</c> directives shown, and a class library can host neither.
/// </remarks>
public static class QuickStartSamples
{
    public static void UnderAHost(IHostApplicationBuilder builder)
    {
        #region sample_quick_start_host

        builder.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "MyScheduler");

            // default max concurrency is 10
            q.UseDefaultThreadPool(maxConcurrency: 5);

            q.UsePersistentStore(store =>
            {
                // there are other databases supported too
                store.UseSqlServer("my connection string");
                store.UseClustering();

                // System.Text.Json is built in; the Newtonsoft one is a package away
                store.UseSystemTextJsonSerializer();

                store.ConfigureStore(options =>
                {
                    // store job data as strings, which avoids surprises when a serialized
                    // type changes shape later
                    options.StoreJobDataAsStrings = true;
                });
            });

            // reads jobs and triggers from JSON; requires the Quartz.Plugins package
            q.UseJsonSchedulingConfiguration(x =>
            {
                x.Files.Add("~/quartz_jobs.json");
                x.FailOnFileNotFound = true;
                x.FailOnSchedulingError = true;
            });
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void ProvisionSchema(IHostApplicationBuilder builder)
    {
        builder.AddQuartz(q =>
        {
            #region sample_quick_start_provision_schema

            q.UsePersistentStore(store =>
            {
                store.UseSqlServer("my connection string");
                store.ProvisionSchema();
            });

            #endregion
        });
    }

    public static async ValueTask WithoutAHost()
    {
        #region sample_quick_start_standalone

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "MyScheduler")
            .UseDefaultThreadPool(maxConcurrency: 5)
            .UseInMemoryStore()
            .BuildScheduler();

        await scheduler.Start();

        #endregion
    }

    public static void FromConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        #region sample_quick_start_from_configuration

        services.AddQuartz(configuration.GetSection("Quartz"));

        #endregion
    }

    public static async ValueTask SchedulingTheJob(IScheduler scheduler)
    {
        #region sample_quick_start_scheduling

        // define the job and tie it to our HelloJob class
        IJobDetail job = JobBuilder.Create<HelloJob>()
            .WithIdentity("job1", "group1")
            .Build();

        // Trigger the job to run now, and then repeat every 10 seconds forever
        // (pass a repeat count as the second argument to stop after a while)
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartNow()
            .WithSimpleSchedule(TimeSpan.FromSeconds(10))
            .Build();

        // Tell Quartz to schedule the job using our trigger
        await scheduler.ScheduleJob(job, trigger);

        // several triggers for one job go together, in one call
        // await scheduler.ScheduleJob(job, [trigger1, trigger2], ScheduleJobOptions.Replacing);

        #endregion
    }
}

#region sample_quick_start_job

public sealed class HelloJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await Console.Out.WriteLineAsync("Greetings from HelloJob!");
    }
}

#endregion
