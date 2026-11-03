#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Runtime.InteropServices;

using Quartz;
using Quartz.Jobs;

// What a net11.0 application built from the packages does that the repository's net10.0 build cannot show.
// Net11ConsumerSmoke waits for each "Net11Consumer:" line below, then asks the dashboard's page for the
// modules it lists and fails on any that does not answer.
//
// - It runs on .NET 11, and says which build.
// - A double in a job's data reads back as the decimal that was written (#4210: .NET 11 rounds a
//   double-to-decimal conversion to the double's exact binary value).
// - A job from Quartz.Jobs runs.
// - The dashboard is served at a custom path, the shape #4224 broke for a Debug build.
Console.WriteLine($"Net11Consumer: running on {RuntimeInformation.FrameworkDescription}");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartz(quartz =>
{
    quartz.AddJobListener<ReportingJobListener>();

    quartz.ScheduleJob<DecimalJob>(
        trigger => trigger.WithIdentity("decimal").StartNow(),
        job => job.WithIdentity("decimal").UsingJobData(DecimalJob.Key, DecimalJob.Written));

    quartz.ScheduleJob<NoOpJob>(
        trigger => trigger.WithIdentity("no-op").StartNow(),
        job => job.WithIdentity("no-op"));
});

builder.AddQuartzHostedService();
builder.Services.AddQuartzDashboard();

WebApplication app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapQuartzDashboard(DashboardPath.Value).AllowAnonymous();

app.Run();

/// <summary>Where the dashboard is mounted: anything but the default <c>/quartz</c>.</summary>
internal static class DashboardPath
{
    public const string Value = "/ops/scheduler";
}

/// <summary>
/// Reads a <see langword="double" /> back as a <see langword="decimal" />, and stops the application with a
/// failing exit code when the value is not the one that was written.
/// </summary>
public sealed class DecimalJob(IHostApplicationLifetime lifetime) : IJob
{
    public const string Key = "amount";
    public const double Written = 12.34d;

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        decimal read = context.MergedJobDataMap.Get<decimal>(Key);
        if (read == 12.34m)
        {
            Console.WriteLine($"Net11Consumer: JobDataMap.Get<decimal> read {Written}d back as {read}");
        }
        else
        {
            Console.WriteLine($"Net11Consumer: JobDataMap.Get<decimal> read {Written}d back as {read}, not 12.34 (#4210)");
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }

        return default;
    }
}

/// <summary>Says when a job has run, so that a job with no output of its own can be waited for.</summary>
public sealed class ReportingJobListener : IJobListener
{
    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(jobException is null
            ? $"Net11Consumer: job {context.JobDetail.Key.Name} ran"
            : $"Net11Consumer: job {context.JobDetail.Key.Name} failed: {jobException}");

        return default;
    }
}
