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

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using static Quartz.Trimming.Canary.Wire.CanarySteps;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The served API's operation policy (#4217): <c>RequireSchedulerRoles</c> over two roles, evaluated by
/// ASP.NET Core's authorization service against every route the client calls, with reflection off.
/// </summary>
/// <remarks>
/// The canary's one caller is this process. A request is an administrator unless it says otherwise in
/// <see cref="RoleHeader" />, so <c>Quartz.HttpClient</c>'s requests pass the policy as they did without
/// it; <see cref="Check" /> then asks as a viewer, and is refused a change.
/// </remarks>
internal static class OperatorRoles
{
    public const string Policy = "QuartzOperators";

    public const string RoleHeader = "X-Canary-Role";

    private const string SchemeName = "canary-role";

    private const string Viewer = "viewer";

    private const string Administrator = "administrator";

    public static void Add(IServiceCollection services)
    {
        services.AddAuthentication(SchemeName).AddScheme<AuthenticationSchemeOptions, CanaryRoleHandler>(SchemeName, configureOptions: null);
        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy.RequireSchedulerRoles(roles =>
        {
            roles.Viewers.Add(Viewer);
            roles.Administrators.Add(Administrator);
        }));
    }

    /// <summary>
    /// A viewer reads the scheduler and is refused a pause, with the API's own words.
    /// </summary>
    public static Task<bool> Check(Uri apiAddress, CancellationToken cancellationToken)
    {
        (string Name, Func<Task<string>> Step)[] steps =
        [
            ("viewer-reads", () => ViewerReads(apiAddress, cancellationToken)),
            ("viewer-refused", () => ViewerIsRefused(apiAddress, cancellationToken)),
        ];

        return CanarySteps.Run("roles", steps);
    }

    private static async Task<string> ViewerReads(Uri apiAddress, CancellationToken cancellationToken)
    {
        using HttpClient client = ViewerClient(apiAddress);
        using HttpResponseMessage response = await client.GetAsync($"schedulers/{Program.SchedulerName}", cancellationToken).ConfigureAwait(false);

        Expect(response.StatusCode == HttpStatusCode.OK, $"a viewer reading the scheduler was answered {(int) response.StatusCode}.");
        return "a viewer reads the scheduler";
    }

    private static async Task<string> ViewerIsRefused(Uri apiAddress, CancellationToken cancellationToken)
    {
        using HttpClient client = ViewerClient(apiAddress);
        using HttpResponseMessage response = await client.PostAsync($"schedulers/{Program.SchedulerName}/pause-all", content: null, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Expect(response.StatusCode == HttpStatusCode.Forbidden, $"a viewer pausing every trigger was answered {(int) response.StatusCode}: {body}");
        Expect(body.Contains("it needs Operate", StringComparison.Ordinal), $"the refusal does not name the level: {body}");
        return "a viewer is refused PauseAll with 403, and the problem details name the level it needs";
    }

    private static HttpClient ViewerClient(Uri apiAddress)
    {
        HttpClient client = new() { BaseAddress = apiAddress };
        client.DefaultRequestHeaders.Add(RoleHeader, Viewer);
        return client;
    }

    /// <summary>
    /// The role a request names in <see cref="RoleHeader" />, or <see cref="Administrator" /> for one that
    /// names none.
    /// </summary>
    private sealed class CanaryRoleHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string role = Request.Headers.TryGetValue(RoleHeader, out Microsoft.Extensions.Primitives.StringValues named) && !string.IsNullOrWhiteSpace(named)
                ? named.ToString()
                : Administrator;

            ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "canary"), new Claim(ClaimTypes.Role, role)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
