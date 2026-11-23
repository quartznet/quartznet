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
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.AspNetCore;

/// <summary>
/// Refuses a request a browser sent from a page of another origin: a request whose <c>Origin</c> header is
/// present and not one of the allowed origins is answered <c>403</c> with problem details.
/// </summary>
/// <remarks>
/// <para>
/// A browser sends <c>Origin</c> on every cross-origin request and on every <c>POST</c>, and a script, a
/// pipeline or an agent sends none, so a request without one passes. What this stops is the request a page
/// the operator happens to visit makes in their name: a plain HTML form posting to <c>…/shutdown</c> needs no
/// CORS preflight, and the browser attaches whatever cookie it holds for the API's site. It stops a DNS
/// rebinding page the same way, which matters where no credential is needed at all.
/// </para>
/// <para>
/// The comparison is exact on <c>scheme://host[:port]</c>, ignoring case, as a browser writes the header:
/// a default port is left out, and <c>Origin: null</c>, which an opaque origin sends, matches nothing.
/// </para>
/// <para>
/// A wrapper of each endpoint's request delegate rather than an endpoint filter, so it runs before anything
/// else the endpoint does — before a policy is asked, which would log a refusal for the caller the page
/// borrowed, and before a body is bound. Applied last, it is the outermost of the wrappers.
/// </para>
/// </remarks>
internal sealed class QuartzRequestGuard
{
    private readonly HashSet<string> allowed;

    /// <param name="allowedOrigins">The origins a browser may call from; none refuses every browser request.</param>
    /// <exception cref="ArgumentException">An entry is not an origin; <see cref="Problem" /> says why.</exception>
    public QuartzRequestGuard(IEnumerable<string> allowedOrigins)
    {
        ArgumentNullException.ThrowIfNull(allowedOrigins);

        allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string origin in allowedOrigins)
        {
            if (Problem(origin) is { } problem)
            {
                throw new ArgumentException(problem, nameof(allowedOrigins));
            }

            allowed.Add(Normalize(origin)!);
        }
    }

    /// <summary>
    /// Puts the guard in front of every endpoint <paramref name="builder" /> builds.
    /// </summary>
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

    /// <summary>
    /// Whether the request may go on: it carries no <c>Origin</c>, or one that is allowed.
    /// </summary>
    public bool Allows(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Headers.TryGetValue(OriginHeader, out Microsoft.Extensions.Primitives.StringValues values))
        {
            return true;
        }

        return values.Count == 1 && Normalize(values[0]) is { } origin && allowed.Contains(origin);
    }

    private async Task Guard(HttpContext context, RequestDelegate next)
    {
        if (Allows(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Through the API's own handler where there is one, so the refusal is the 9005 Warning every
        // configured rule that says no is, in the API's category; the detail is the caller's own header.
        ForbiddenException refusal = new($"Requests from origin {context.Request.Headers[OriginHeader]} are not allowed here.");
        IResult result = context.RequestServices.GetService<ExceptionHandler>()?.HandleException(refusal, context)
                         ?? ContractProblem.Problem(refusal.Message, StatusCodes.Status403Forbidden);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Why <paramref name="origin" /> is not an origin, or <see langword="null" /> when it is one: an absolute
    /// <c>http</c> or <c>https</c> address with a host and nothing after it but an optional <c>/</c>.
    /// </summary>
    public static string? Problem(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return "An allowed origin is blank.";
        }

        if (Normalize(origin) is null)
        {
            return $"'{origin}' is not an origin: write scheme://host[:port], http or https, with no path, query or fragment, "
                   + "as a browser sends it in the Origin header — for example https://ops.example.com.";
        }

        return null;
    }

    /// <summary>
    /// <c>scheme://host[:port]</c> as a browser writes it, or <see langword="null" /> for a value that is not
    /// an origin.
    /// </summary>
    internal static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private const string OriginHeader = "Origin";
}
