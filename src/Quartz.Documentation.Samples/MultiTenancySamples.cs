using System.Collections.Specialized;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;
using Quartz.Plugins.Json;

namespace Quartz.Documentation.Samples.Tenancy;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/multi-tenancy.md.
/// </summary>
/// <remarks>
/// In a namespace of its own so the tenant-shaped job types at the bottom of the file can carry the
/// names the page uses — <c>ReportJob</c> in particular — without colliding with the shared ones in
/// <c>SampleTypes.cs</c>.
/// </remarks>
public static class MultiTenancySamples
{
    public static void SchedulerPerTenant(
        IHostApplicationBuilder builder,
        IReadOnlyList<string> tenants,
        IReadOnlyDictionary<string, string> connectionStrings)
    {
        #region sample_tenancy_scheduler_per_tenant

        foreach (string tenant in tenants)
        {
            builder.Services.AddQuartz(tenant, q =>
            {
                q.UsePersistentStore(s =>
                {
                    s.UseSqlServer(connectionStrings[tenant]);
                    s.UseClustering();
                });
                q.UseDefaultThreadPool(maxConcurrency: 5);
                q.AddJob<NightlyReportJob>(j => j.WithIdentity("nightly"));
                q.AddTrigger<NightlyReportJob>(t => t.WithCronSchedule("0 30 2 * * ?"));
            });
        }

        builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

        #endregion
    }

    #region sample_tenancy_inject_named

    public sealed class TenantOpsService([FromKeyedServices("acme")] IScheduler scheduler);

    #endregion

    public static void ResolveNamed(IServiceProvider provider, string tenant)
    {
        #region sample_tenancy_resolve_named

        IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>(tenant);

        #endregion
    }

    public static async Task ListRegistrations(IServiceProvider provider)
    {
        #region sample_tenancy_scheduler_registry

        ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();

        foreach (SchedulerRegistration tenant in await registry.QuerySchedulers())
        {
            Console.WriteLine($"{tenant.Name}: {tenant.Status?.ToString() ?? "registered, not created"}");
        }

        #endregion
    }

    public static void PerTenantClock(IHostApplicationBuilder builder, TimeProvider acmeClock)
    {
        #region sample_tenancy_time_provider

        builder.Services.AddQuartz("acme", q => q.UseTimeProvider(acmeClock));

        #endregion
    }

    public static void PerTenantJobTypes(IHostApplicationBuilder builder)
    {
        #region sample_tenancy_job_types

        builder.Services.AddQuartz("acme", q =>
        {
            q.AddJobType<ReportJob, AcmeReportJob>();            // a different implementation
            q.AddJobType<AuditJob>(ServiceLifetime.Singleton);   // a different lifetime
            q.AddJobType<ExportJob>(sp => new ExportJob(sp.GetRequiredKeyedService<IExportSink>("acme")));

            q.AddJob<ReportJob>(j => j.WithIdentity("report"));
        });

        #endregion
    }

    #region sample_tenancy_job_reads_its_scheduler

    public sealed class RotateTenantKeysJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            // The scheduler running this fire, whichever tenant it belongs to. An injected
            // ISchedulerFactory would have been the default scheduler's.
            IScheduler mine = context.Scheduler;

            await mine.PauseTriggers(
                GroupMatcher<TriggerKey>.GroupEquals(context.Trigger.Key.Group),
                cancellationToken);
        }
    }

    #endregion

    public static void JobConstructedWithItsSchedulersParts(IHostApplicationBuilder builder)
    {
        #region sample_tenancy_job_built_by_its_scheduler

        builder.Services.AddQuartz("acme", q =>
        {
            // A job the container builds may not take a scheduler's parts by constructor - startup
            // refuses one that does. The job that genuinely has to be given them is built here instead,
            // where the scheduler's key is known.
            q.AddJobType<ArchiveJob>(provider =>
                new ArchiveJob(provider.GetRequiredKeyedService<ISchedulerFactory>("acme")));

            q.AddJob<ArchiveJob>(j => j.WithIdentity("archive"));
        });

        #endregion
    }

    public static void PluginNamedByProperties(IHostApplicationBuilder builder)
    {
        #region sample_tenancy_plugin_by_properties

        builder.Services.AddQuartz("acme", new NameValueCollection
        {
            ["quartz.plugin.json.type"] = typeof(JsonSchedulingDataProcessorPlugin).AssemblyQualifiedName,
            ["quartz.plugin.json.fileNames"] = "acme-jobs.json",
        });

        #endregion
    }

    public static void ConfigureEveryScheduler(IHostApplicationBuilder builder, string acme)
    {
        #region sample_tenancy_configure_all

        builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s => s.UseSqlServer(acme)));
        builder.Services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));

        // Every scheduler above, and every scheduler registered after this line
        builder.Services.ConfigureAllQuartzSchedulers(q =>
        {
            q.AddPlugin<TenantAuditPlugin>();
            q.AddJobListener<AuditListener>();
        });

        #endregion
    }

    public static void PerTenantHealthCheck(IHostApplicationBuilder builder)
    {
        #region sample_tenancy_health_checks

        builder.Services.AddQuartz("acme", q => q.AddQuartzHealthChecks(o => o.Tags.Add("tenant:acme")));

        #endregion
    }

    public static void GroupPerTenant(string tenantId)
    {
        #region sample_tenancy_group_keys

        JobKey job = new("nightly-report", tenantId);
        TriggerKey trigger = new("nightly", tenantId);

        #endregion
    }

    public static async Task GroupScopedQueries(IScheduler scheduler, string tenantId)
    {
        #region sample_tenancy_group_matchers

        // everything this tenant has scheduled
        PagedResult<TriggerHeader> theirs = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupEquals(tenantId),
            Take = 100,
            IncludeTotalCount = true,
        });

        // suspend a tenant
        List<string> paused = await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(tenantId));

        // is a tenant suspended?
        PagedResult<TriggerGroup> group = await scheduler.QueryTriggerGroups(
            new TriggerGroupQuery { Name = tenantId, Take = 1 });
        bool suspended = group.Items is [{ Paused: true }];

        #endregion
    }

    public static void PerTenantListener(IQuartzBuilder q, string tenantId)
    {
        #region sample_tenancy_group_listener

        q.AddJobListener<AuditListener>(Matchers.Group<JobKey>(StringOperator.Equality, tenantId));

        #endregion
    }

    public static void PerTenantQuotas(IQuartzBuilder q)
    {
        #region sample_tenancy_execution_limits

        q.UseExecutionLimits(limits => limits
            .UseTriggerGroupWhenUnset()
            .ForGroup("acme", 8, ExecutionLimitScope.Cluster)          // a big tenant
            .ForGroup("initech", 2, ExecutionLimitScope.Cluster)
            .ForOtherGroups(1, ExecutionLimitScope.Cluster));          // everyone else gets one thread each

        #endregion
    }

    public static void PerTenantTablePrefix(IHostApplicationBuilder builder, string sharedConnectionString)
    {
        #region sample_tenancy_table_prefix

        builder.Services.AddQuartz("acme", q => q.UsePersistentStore(s =>
        {
            s.UseSqlServer(sharedConnectionString);
            s.ConfigureStore(o => o.TablePrefix = "ACME_QRTZ_");
        }));

        #endregion
    }

    #region sample_tenancy_ambient_tenant

    public static class TenantContext
    {
        private static readonly AsyncLocal<string?> current = new();

        public static string? Current
        {
            get => current.Value;
            internal set => current.Value = value;
        }
    }

    #endregion

    public static void PrepareJobScope(IQuartzBuilder q)
    {
        #region sample_tenancy_configure_job_scope

        q.ConfigureJobScope((scope, bundle, scheduler) =>
        {
            TenantContext.Current = bundle.Trigger.Key.Group;
        });

        #endregion
    }

    #region sample_tenancy_execution_context_accessor

    public sealed class TenantConnectionFactory(
        IJobExecutionContextAccessor accessor,
        IReadOnlyDictionary<string, string> connectionStrings)
    {
        public string ConnectionString =>
            connectionStrings[accessor.Current?.Trigger.Key.Group
                ?? throw new InvalidOperationException("no job is running on this flow")];
    }

    #endregion

    public static async Task OnboardAtRuntime(
        IHost app,
        string tenantId,
        IReadOnlyDictionary<string, string> connectionStrings,
        Dictionary<string, StandaloneSchedulerFactory> tenantFactories)
    {
        #region sample_tenancy_runtime_onboarding

        StandaloneSchedulerFactory tenantFactory = QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(o => o.InstanceName = tenantId)
                .UsePersistentStore(s => s.UseSqlServer(connectionStrings[tenantId])))
            .Build();

        IScheduler tenant = await tenantFactory.GetScheduler();
        await tenant.Start();

        tenantFactories[tenantId] = tenantFactory;
        app.Services.GetRequiredService<ISchedulerRepository>().Bind(tenant);

        #endregion
    }

    public static async Task Offboard(
        IHost app,
        string tenantId,
        Dictionary<string, StandaloneSchedulerFactory> tenantFactories)
    {
        #region sample_tenancy_offboarding

        StandaloneSchedulerFactory tenantFactory = tenantFactories[tenantId];
        tenantFactories.Remove(tenantId);

        // Disposal shuts the scheduler down without waiting for its jobs, so ask for the wait here.
        IScheduler tenant = await tenantFactory.GetScheduler();
        await tenant.Shutdown(waitForJobsToComplete: true);

        await tenantFactory.DisposeAsync();
        app.Services.GetRequiredService<ISchedulerRepository>().Remove(tenantId);

        #endregion
    }

    #region sample_tenancy_scheduler_authorization_handler

    // What "may act on this scheduler" means is the application's to decide, so the requirement itself
    // says nothing and the handler says everything.
    public sealed class SchedulerOwnerRequirement : IAuthorizationRequirement;

    public sealed class SchedulerOwnerHandler : AuthorizationHandler<SchedulerOwnerRequirement, SchedulerResource>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SchedulerOwnerRequirement requirement,
            SchedulerResource resource)
        {
            string? tenant = context.User.FindFirst("tenant")?.Value;

            // Scheduler names are compared ignoring case everywhere else in Quartz, so compare them that
            // way here too - otherwise "Acme" and "acme" are the same scheduler and different tenants.
            if (string.Equals(tenant, resource.SchedulerName, StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    #endregion

    public static void SchedulerAuthorization(IHostApplicationBuilder builder)
    {
        #region sample_tenancy_scheduler_authorization

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("SchedulerOwner", policy => policy.AddRequirements(new SchedulerOwnerRequirement()));

        builder.Services.AddSingleton<IAuthorizationHandler, SchedulerOwnerHandler>();

        // One policy, one handler, both surfaces: the API answers 403 for a scheduler the caller fails
        // for, and the dashboard offers them only the schedulers they pass for.
        builder.Services.AddQuartzHttpApi(options => options.SchedulerAuthorizationPolicy = "SchedulerOwner");
        builder.Services.AddQuartzDashboard(options => options.SchedulerAuthorizationPolicy = "SchedulerOwner");

        #endregion
    }
}

/// <summary>
/// The tenant-shaped jobs and listeners the samples above name.
/// </summary>
public sealed class NightlyReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <inheritdoc cref="NightlyReportJob" />
public class ReportJob : IJob
{
    public virtual ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <inheritdoc cref="NightlyReportJob" />
public sealed class AcmeReportJob : ReportJob;

/// <inheritdoc cref="NightlyReportJob" />
public sealed class AuditJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <inheritdoc cref="NightlyReportJob" />
public interface IExportSink;

/// <inheritdoc cref="NightlyReportJob" />
public sealed class ExportJob(IExportSink sink) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <inheritdoc cref="NightlyReportJob" />
public sealed class ArchiveJob(ISchedulerFactory schedulerFactory) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

/// <inheritdoc cref="NightlyReportJob" />
public sealed class AuditListener : IJobListener;

/// <inheritdoc cref="NightlyReportJob" />
public sealed class TenantAuditPlugin : ISchedulerPlugin
{
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default) => default;

    public ValueTask Start(CancellationToken cancellationToken = default) => default;

    public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
}
