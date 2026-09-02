IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// A password the AppHost does not invent afresh on every run. The container and its volume outlive the
// AppHost (see below), so a generated password would leave run two holding a credential the surviving
// database has never heard of - and the symptom is not an error but a worker that never starts, because
// WaitFor never completes. A parameter with a value is the Aspire idiom for pinning one; a real
// deployment supplies it from user secrets or the environment
// (`builder.AddParameter("postgres-password", secret: true)`), which is what the AppHost warns about
// when nothing does.
IResourceBuilder<ParameterResource> postgresPassword =
    builder.AddParameter("postgres-password", "quartz-examples-local-only", secret: true);

// A data volume and a persistent container, because a persistent job store whose rows disappear
// between runs is an in-memory store with more moving parts.
IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// The name given here is the name the worker asks AddQuartzPersistentStore for: WithReference below
// injects it as ConnectionStrings__quartz, and the default configuration builder reads that back as
// ConnectionStrings:quartz.
IResourceBuilder<PostgresDatabaseResource> quartzDb = postgres.AddDatabase("quartz");

// WithHttpHealthCheck needs the resource to have an http or https endpoint, and throws at AppHost
// build time when it does not. The worker's http endpoint comes from its launch profile, which is
// why that project has a Properties/launchSettings.json and the other examples' workers do not.
builder.AddProject<Projects.Quartz_Examples_Aspire_Worker>("worker")
    .WithReference(quartzDb)
    .WaitFor(quartzDb)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
