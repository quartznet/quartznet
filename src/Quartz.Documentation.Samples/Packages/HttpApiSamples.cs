using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/http-api.md.
/// </summary>
public static class HttpApiSamples
{
    public static void Registration(string[] args)
    {
        #region sample_httpapi_registration

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddQuartzHttpApi();

        builder.AddQuartz(q => { });
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void Pipeline(WebApplicationBuilder builder)
    {
        #region sample_httpapi_pipeline

        WebApplication app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapQuartzHttpApi("/quartz-api").RequireAuthorization();

        #endregion
    }

    public static void ApiPath(WebApplicationBuilder builder, WebApplication app)
    {
        #region sample_httpapi_path

        // at the map site, beside the application's other routes
        app.MapQuartzHttpApi("/ops/api");

        // or at registration
        builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");

        #endregion
    }

    public static async ValueTask ClientAgainstTheApi(System.Net.Http.HttpClient httpClient)
    {
        #region sample_httpapi_client

        IScheduler scheduler = new HttpScheduler("MyScheduler", httpClient);
        await scheduler.TriggerJob(new JobKey("nightly-report"));

        #endregion
    }

    public static void ClientRegistration(WebApplicationBuilder builder)
    {
        #region sample_httpapi_client_registration

        builder.Services.AddHttpClient("quartz", client => client.BaseAddress = new Uri("https://scheduler.example.com/"));
        builder.Services.AddQuartzHttpClient(schedulerName: "MyScheduler", httpClientName: "quartz");

        #endregion
    }
}
