using System.Net.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Serialization.SystemTextJson;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/http-client.md.
/// </summary>
public static class HttpClientSamples
{
    public static void ServerSide(WebApplicationBuilder builder, WebApplication app)
    {
        #region sample_httpclient_server_side

        builder.Services.AddQuartzHttpApi();
        // ...
        app.MapQuartzHttpApi("/quartz-api");

        #endregion
    }

    public static void Registration(WebApplicationBuilder builder)
    {
        #region sample_httpclient_registration

        builder.Services.AddHttpClient("quartz", client =>
        {
            client.BaseAddress = new Uri("https://scheduler.example.com/quartz-api/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");

        #endregion
    }

    /// <summary>
    /// Two containers so the page can show one controller name twice, which is how it contrasts the
    /// single-scheduler and keyed shapes.
    /// </summary>
    public static class Controllers
    {
        #region sample_httpclient_controller

        public sealed class OpsController(IScheduler scheduler);                          // one scheduler

        #endregion
    }

    public static class KeyedControllers
    {
        #region sample_httpclient_keyed_controller

        public sealed class OpsController([FromKeyedServices("MyScheduler")] IScheduler scheduler);

        #endregion
    }

    public static void ResolveKeyed(IServiceProvider provider)
    {
        #region sample_httpclient_resolve_keyed

        IScheduler reporting = provider.GetRequiredKeyedService<IScheduler>("reporting");

        #endregion
    }

    public static async ValueTask WithoutTheContainer()
    {
        #region sample_httpclient_without_container

        using HttpClient http = new() { BaseAddress = new Uri("https://scheduler.example.com/quartz-api/") };
        IScheduler scheduler = new HttpScheduler("MyScheduler", http);

        await scheduler.TriggerJob(new JobKey("nightly-report", "reports"));

        #endregion
    }

    public static void CustomTriggerSerializers(HttpClient http)
    {
        #region sample_httpclient_custom_serializers

        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTriggerSerializer<MyTrigger>(new MyTriggerSerializer());

        IScheduler scheduler = new HttpScheduler("MyScheduler", http, jsonSerializerOptions: null, registry);

        #endregion
    }

    public static async ValueTask Metadata(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_httpclient_metadata

        SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken);

        #endregion
    }

    public static async ValueTask QueryTriggers(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_httpclient_query_triggers

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
        {
            Group = GroupMatcher<TriggerKey>.GroupStartsWith("reporting-"),
            State = TriggerState.Error,
            Skip = 0,
            Take = 100,
            IncludeTotalCount = true,
        }, cancellationToken);

        #endregion
    }

    public static async ValueTask BulkFetch(IScheduler scheduler, IReadOnlyCollection<JobKey> keys, CancellationToken cancellationToken)
    {
        #region sample_httpclient_bulk_fetch

        List<IJobDetail> details = await scheduler.GetJobDetails(keys, cancellationToken);

        #endregion
    }
}
