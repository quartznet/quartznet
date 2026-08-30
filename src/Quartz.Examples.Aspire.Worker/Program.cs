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
builder.AddQuartzPersistentStore("quartz");

builder.AddQuartz(q => q.ScheduleJob<HeartbeatJob>(trigger => trigger
    .WithIdentity("heartbeat")
    .StartNow()
    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())));

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

WebApplication app = builder.Build();

// What MapDefaultEndpoints() would map. The check itself was registered by AddQuartzPersistentStore.
app.MapHealthChecks("/health");

app.Run();
