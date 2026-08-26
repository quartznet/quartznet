using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What the options validator says about a per-scheduler policy the container cannot evaluate.
/// </summary>
/// <remarks>
/// A policy name nothing knows is ASP.NET Core's to complain about, and it does, at the first request
/// that names it. A container with no authorization services at all is different: nothing would ever
/// complain, and the setting would read as enforced while enforcing nothing.
/// </remarks>
public class SchedulerAuthorizationOptionsTest
{
    [Test]
    public async Task APolicyWithNoAuthorizationServicesToEvaluateItIsRefusedAtStartup()
    {
        await using WebApplication app = CreateApp(authorization: false);

        Action act = () => _ = app.Services.GetRequiredService<IOptions<QuartzHttpApiOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*AddAuthorization*",
                "a security setting that silently does nothing is worse than one that is missing, so the "
                + "message says what to call");
    }

    [Test]
    public async Task APolicyIsAcceptedWhenTheContainerCanEvaluateIt()
    {
        await using WebApplication app = CreateApp(authorization: true);

        QuartzHttpApiOptions options = app.Services.GetRequiredService<IOptions<QuartzHttpApiOptions>>().Value;

        options.SchedulerAuthorizationPolicy.Should().Be(TenantAuthenticationExtensions.SchedulerOwnerPolicy);
    }

    [Test]
    public async Task NoPolicyNeedsNoAuthorizationServices()
    {
        await using WebApplication app = CreateApp(authorization: false, policyName: null);

        QuartzHttpApiOptions options = app.Services.GetRequiredService<IOptions<QuartzHttpApiOptions>>().Value;

        options.SchedulerAuthorizationPolicy.Should().BeNull(
            "an API that configured no per-scheduler policy asks nothing of the container");
    }

    private static WebApplication CreateApp(
        bool authorization,
        string? policyName = TenantAuthenticationExtensions.SchedulerOwnerPolicy)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddQuartz();

        if (authorization)
        {
            builder.Services.AddTenantAuthorization();
        }

        builder.Services.AddQuartzHttpApi(options => options.SchedulerAuthorizationPolicy = policyName);
        return builder.Build();
    }
}
