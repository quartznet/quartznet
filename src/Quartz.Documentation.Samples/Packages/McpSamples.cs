using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Packages;

public static class McpSamples
{
    public static void Setup(string[] args)
    {
        #region sample_mcp_setup

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.AddQuartz();
        builder.AddQuartzHostedService();

        // Who the caller is comes from the application's own bearer scheme — AddJwtBearer, an API key
        // handler — and never from a cookie, which a browser sends on its own.

        // The policy the dashboard and the HTTP API name, so one set of roles decides all three.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("Quartz.Operations", policy => policy.RequireSchedulerRoles(roles =>
            {
                roles.Viewers.Add("ops-viewer");
                roles.Operators.Add("ops-operator");
                roles.Administrators.Add("ops-admin");
            }));

        builder.Services.AddQuartzMcp(options =>
        {
            options.ReadOnly = false;
            options.OperationAuthorizationPolicy = "Quartz.Operations";
        });

        WebApplication app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        // At /quartz-mcp; MapQuartzMcp("/ops/mcp") names another path.
        app.MapQuartzMcp().RequireAuthorization();

        #endregion
    }

    public static void RateLimiting(WebApplicationBuilder builder)
    {
        #region sample_mcp_rate_limiting

        // MaxChangesPerMinute bounds changes per caller. A limit on every request is ASP.NET Core's own.
        builder.Services.AddRateLimiter(limiter => limiter.AddFixedWindowLimiter("mcp", window =>
        {
            window.PermitLimit = 120;
            window.Window = TimeSpan.FromMinutes(1);
        }));

        WebApplication app = builder.Build();
        app.UseRateLimiter();
        app.MapQuartzMcp().RequireAuthorization().RequireRateLimiting("mcp");

        #endregion
    }

    public static void KeyRing(WebApplicationBuilder builder)
    {
        #region sample_mcp_key_ring

        // A confirmation token from one instance is read by another only over a shared key ring.
        builder.Services.AddDataProtection()
            .SetApplicationName("ops-scheduler")
            .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/ops-scheduler/keys"));

        #endregion
    }
}
