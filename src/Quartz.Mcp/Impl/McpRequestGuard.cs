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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

using Quartz.AspNetCore;
using Quartz.AspNetCore.HttpApi.Util;

namespace Quartz.Mcp.Impl;

/// <summary>
/// What the MCP endpoint refuses before the SDK reads a request: an <c>Origin</c> that is not allowed, by the HTTP
/// API's own <see cref="QuartzRequestGuard" />, and a <c>POST</c> whose body is not declared JSON.
/// </summary>
/// <remarks>
/// <para>
/// A cross-site page can send <c>fetch(…, { mode: "no-cors", credentials: "include" })</c> with a
/// <c>text/plain</c> body and an <c>Accept</c> listing <c>application/json</c> and <c>text/event-stream</c>: both
/// headers are CORS-safelisted, so it is a simple request with no preflight, and the SDK checks <c>Accept</c> but
/// neither <c>Origin</c> nor <c>Content-Type</c>. Refusing a body that is not <c>application/json</c> with
/// <c>415</c> makes every request that gets through one a browser would have had to preflight, and the
/// <c>Origin</c> check refuses the page itself: the MCP specification asks for exactly that check.
/// </para>
/// <para>
/// A wrapper of each endpoint's request delegate, as the HTTP API's guard is, so it runs before anything the SDK
/// does, and its refusal is logged as <c>9504</c>.
/// </para>
/// </remarks>
internal sealed class McpRequestGuard
{
    private readonly QuartzRequestGuard origins;
    private readonly ILogger logger;

    public McpRequestGuard(IEnumerable<string> allowedOrigins, ILoggerFactory loggerFactory)
    {
        origins = new QuartzRequestGuard(allowedOrigins);
        logger = loggerFactory.CreateLogger(McpLog.Category);
    }

    public void Apply(IEndpointConventionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpointBuilder =>
        {
            RequestDelegate next = endpointBuilder.RequestDelegate
                ?? throw new InvalidOperationException($"Endpoint {endpointBuilder.DisplayName} has null RequestDelegate");

            endpointBuilder.RequestDelegate = context => Guard(context, next);
        });
    }

    private async Task Guard(HttpContext context, RequestDelegate next)
    {
        if (!origins.Allows(context))
        {
            logger.RequestRefused("an Origin that is not allowed");
            await ContractProblem.Problem(
                $"Requests from origin {context.Request.Headers.Origin} are not allowed here.",
                StatusCodes.Status403Forbidden).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsPost(context.Request.Method) && !IsJson(context.Request.ContentType))
        {
            logger.RequestRefused("a body that is not application/json");
            await ContractProblem.Problem(
                "The MCP endpoint takes a JSON-RPC body with Content-Type: application/json.",
                StatusCodes.Status415UnsupportedMediaType).ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    internal static bool IsJson(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
               && parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
