using Microsoft.AspNetCore.Builder;

namespace Quartz.AspNetCore.HttpApi;

internal class QuartzApiConventionBuilder : IEndpointConventionBuilder
{
    private readonly RouteHandlerBuilder[] endpoints;

    public QuartzApiConventionBuilder(IEnumerable<RouteHandlerBuilder> endpoints)
    {
        this.endpoints = endpoints.ToArray();
    }

    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Add(convention);
        }
    }
}