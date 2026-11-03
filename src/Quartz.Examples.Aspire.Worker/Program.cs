using Quartz;
using Quartz.Examples.Aspire.Worker;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// A generated Aspire solution calls builder.AddServiceDefaults() here, from the ServiceDefaults
// project its template writes. This repository carries no such project, and nothing on this page
// needs one: ServiceDefaults adds the OTLP exporter, service discovery and HTTP resilience, none of
// which the line below depends on. Quartz's own signals are subscribed by AddQuartzPersistentStore
// whether ServiceDefaults ran or not.

// Registered first, on purpose: AddQuartzPersistentStore decides where connections come from against
// the services registered so far, so a data source that arrives afterwards is one it never sees.
builder.AddNpgsqlDataSource("quartz");

// Everything the Aspire connection named "quartz" is evidence of: which database it is, the driver
// delegate that speaks its SQL, where connections come from, and the scheduler's health check.
//
// The tables come with it here. This profile runs in Development, where the store creates whatever
// its schema is missing rather than refusing to start against the empty database a fresh Postgres
// volume hands it. Anywhere else it would validate instead, and something else would apply the
// schema; settings.ProvisionSchema says which without asking the environment.
builder.AddQuartzPersistentStore("quartz");

builder.AddQuartz(q =>
{
    q.ScheduleJob<HeartbeatJob>(trigger => trigger
        .WithIdentity("heartbeat")
        .StartNow()
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()));

    // Dials this scheduler out to the Quartz dashboard the AppHost added, as an agent. Where it is, the
    // token and the name to register under arrive as Quartz__DashboardAgent__* from the AppHost's
    // WithReference(dashboard). The agent accepts no job type by name until told which: the dashboard can
    // pause, resume and trigger the heartbeat, and cannot add a job of a type it names.
    q.UseDashboardAgent();
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

WebApplication app = builder.Build();

// What MapDefaultEndpoints() would map. The check itself was registered by AddQuartzPersistentStore.
app.MapHealthChecks("/health");

app.Run();
