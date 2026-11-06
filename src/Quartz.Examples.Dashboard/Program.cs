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

using Quartz;
using Quartz.Examples.Dashboard;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Anonymous access is only for this local demo, not a production deployment.
builder.WebHost.UseUrls("http://localhost:5088");

builder.Services.AddQuartz(quartz =>
{
    quartz.ConfigureScheduler(options =>
    {
        options.InstanceName = "Dashboard Demo";
        options.ShutdownJobInterruption = ShutdownJobInterruption.Always;
    });
    quartz.UseInMemoryStore();
    quartz.UseDefaultThreadPool(maxConcurrency: 10);

    AddDemoJob(quartz, "housekeeping", "Poll-inbox", durationSeconds: 1, intervalSeconds: 5);
    AddDemoJob(quartz, "housekeeping", "Refresh-catalog", durationSeconds: 2, intervalSeconds: 7);
    AddDemoJob(quartz, "orders", "Export-orders", durationSeconds: 12, intervalSeconds: 20);
    AddDemoJob(quartz, "reporting", "Build-report", durationSeconds: 25, intervalSeconds: 35);
    AddDemoJob(quartz, "integrations", "Sync-inventory", durationSeconds: 5, intervalSeconds: 15);
    AddDemoJob(quartz, "integrations", "Failing-webhook", durationSeconds: 3, intervalSeconds: 18, fail: true);
});

// The dashboard automatically records real executions, including the intentional failures.
builder.Services.AddQuartzDashboard();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

WebApplication app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/", () => Results.Redirect("/quartz/timeline"));
app.MapQuartzDashboard().AllowAnonymous();

app.Run();

static void AddDemoJob(
    IQuartzBuilder quartz,
    string group,
    string name,
    int durationSeconds,
    int intervalSeconds,
    bool fail = false)
{
    JobKey jobKey = new JobKey(name, group);

    quartz.AddJob<DemoJob>(job => job
        .WithIdentity(jobKey)
        .WithDescription(fail ? "Intentional failure to demonstrate error history" : "Local dashboard demo job")
        .UsingJobData("DurationSeconds", durationSeconds)
        .UsingJobData("Fail", fail));

    quartz.AddTrigger(trigger => trigger
        .WithIdentity($"{name}-interval", group)
        .ForJob(jobKey)
        .StartNow()
        .WithSimpleSchedule(schedule => schedule
            .WithInterval(TimeSpan.FromSeconds(intervalSeconds))
            .RepeatForever()));
}
