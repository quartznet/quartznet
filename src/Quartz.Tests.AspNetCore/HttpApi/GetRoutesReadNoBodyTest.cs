#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// No GET route of the HTTP API declares a request body (#4280).
/// </summary>
/// <remarks>
/// A <c>string[]</c> handler parameter with no binding attribute was inferred as an optional JSON body on
/// <c>history/executions</c>, <c>history/misfires</c> and <c>history/statistics</c>. The route then
/// carried accepts metadata, and in a host that also maps Razor Pages and the dashboard, routing rejected
/// every GET sent without a <c>Content-Type</c>. The request fell through to a bare 404, so a remote
/// dashboard read "this target serves no history". This test host matched those GETs anyway, which is why
/// the request tests stayed green. The metadata is wrong in any host, so this checks the metadata.
/// </remarks>
public sealed class GetRoutesReadNoBodyTest
{
    [Test]
    public void NoGetRouteAcceptsABody()
    {
        TestContentRoot.Apply();
        using WebApplicationFactory<Program> factory = new();

        List<RouteEndpoint> getRoutes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is [ "GET" ])
            .ToList();

        getRoutes.Should().NotBeEmpty("the HTTP API maps its read routes as GET");
        getRoutes.Should().Contain(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/history/executions", StringComparison.Ordinal),
            "the history routes are among the ones this guards");

        List<string> withBody = getRoutes
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText + " accepts " + endpoint.Metadata.GetMetadata<IAcceptsMetadata>()!.RequestType)
            .ToList();

        withBody.Should().BeEmpty(
            "a GET carries no body, and a route that declares one is rejected by routing in some hosts when the "
            + "request has no Content-Type; bind array parameters with [FromQuery]");
    }
}
