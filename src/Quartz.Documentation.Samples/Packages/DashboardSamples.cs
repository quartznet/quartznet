using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/dashboard.md.
/// </summary>
public static class DashboardSamples
{
    public static void Registration(string[] args)
    {
        #region sample_dashboard_registration

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.AddQuartz();

        builder.Services.AddQuartzHttpApi(options =>
        {
            options.ApiPath = "/quartz-api";
        });

        builder.Services.AddQuartzDashboard();
        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void Pipeline(WebApplicationBuilder builder)
    {
        #region sample_dashboard_pipeline

        WebApplication app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapQuartzHttpApi().RequireAuthorization();
        app.MapQuartzDashboard().RequireAuthorization();

        #endregion
    }

    public static void MapAtAPath(WebApplication app)
    {
        #region sample_dashboard_map_path

        app.MapQuartzDashboard("/my-api/quartz").RequireAuthorization();

        #endregion
    }

    public static void PathFromOptions(IServiceCollection services)
    {
        #region sample_dashboard_options_path

        services.AddQuartzDashboard(options =>
        {
            options.DashboardPath = "/my-api/quartz";
        });

        #endregion
    }

    public static void PathBase(WebApplication app)
    {
        #region sample_dashboard_path_base

        app.UsePathBase("/my-api");
        app.UseRouting();

        #endregion
    }

    public static void HistoryPlugins(WebApplicationBuilder builder)
    {
        #region sample_dashboard_history_plugins

        builder.AddQuartz(q =>
        {
            q.UseJobHistoryLogging();
            q.UseTriggerHistoryLogging();
        });

        #endregion
    }

    public static void HistoryBounds(IServiceCollection services)
    {
        #region sample_dashboard_history_bounds

        services.AddQuartzDashboard(options =>
        {
            options.HistoryRetention = TimeSpan.FromHours(6);
            options.HistoryMaxEntriesPerScheduler = 500;
        });

        #endregion
    }

    public static void AuthorizationPolicy(WebApplicationBuilder builder)
    {
        #region sample_dashboard_authorization_policy

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("QuartzDashboardOps", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole("Operations", "SchedulerAdmin");
            });
        });

        builder.Services.AddQuartzDashboard(options =>
        {
            options.AuthorizationPolicy = "QuartzDashboardOps";
        });

        #endregion
    }

    public static void RequireAuthorization(WebApplication app)
    {
        #region sample_dashboard_require_authorization

        app.MapQuartzHttpApi().RequireAuthorization("QuartzDashboardOps");
        app.MapQuartzDashboard();

        #endregion
    }

    public static void HostAppRegistration(WebApplicationBuilder builder)
    {
        #region sample_dashboard_host_app_registration

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddQuartzDashboard();

        #endregion
    }

    public static void HostAppPipeline(WebApplication app)
    {
        #region sample_dashboard_host_app_pipeline

        app.UseAntiforgery();

        RazorComponentsEndpointConventionBuilder blazor = app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapQuartzHttpApi().RequireAuthorization();
        app.MapQuartzDashboard(blazor).RequireAuthorization();

        #endregion
    }
}
