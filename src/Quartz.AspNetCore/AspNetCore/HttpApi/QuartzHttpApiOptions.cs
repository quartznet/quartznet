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

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz;

/// <summary>
/// How the HTTP API is served.
/// </summary>
/// <remarks>
/// There is one set of these per process, not one per scheduler. The API serves every scheduler in the
/// container through one set of endpoints — a request names the scheduler it is for — so what is
/// configured here describes the endpoints rather than any scheduler. That is why there is no
/// <c>IQuartzBuilder</c> registration: calling <c>services.AddQuartzHttpApi(configure)</c> twice configures
/// the same options twice, and the last callback registered wins for any setting both of them touch.
/// </remarks>
public sealed class QuartzHttpApiOptions
{
    internal const string DefaultApiPath = "/quartz-api";

    /// <summary>
    /// The path the API is served under. It is a property of the process, not of a scheduler: every
    /// scheduler is reached under this one path.
    /// </summary>
    /// <remarks>
    /// <c>MapQuartzHttpApi(pattern)</c> says the same thing where the endpoints are mapped, which is
    /// where the rest of an application's routes are written, and a pattern given there wins over this.
    /// </remarks>
    public string ApiPath { get; set; } = DefaultApiPath;

    /// <summary>
    /// Whether a failure's stack trace is included in the problem details returned to the caller.
    /// </summary>
    /// <remarks>
    /// It also puts the real message of a <c>500</c> back in the response body. Both are things to read
    /// while developing and neither is something to ship: a fault's message routinely names the server,
    /// the database, the login or the constraint that produced it.
    /// </remarks>
    public bool IncludeStackTraceInProblemDetails { get; set; }

    /// <summary>
    /// The most items one paged request may return: 1000 by default, and <c>0</c> for no limit.
    /// A <c>take</c> naming a number above it is a <c>400</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page size is the only thing on this API a caller can use to make the server do arbitrary work,
    /// and until 4.0.0-beta.1 nothing bounded it: one request could materialize every trigger in the
    /// store while the bulk key fetch next door refused 1001 keys. The default is that same 1000.
    /// </para>
    /// <para>
    /// <c>?take=all</c> is bounded by this rather than refused by it. It does not name a number — it says
    /// "as many as you will give me" — so it is answered with this many, and <c>hasMore</c> says whether
    /// that was all of them. A listing whose matches fit under the cap therefore answers exactly as it
    /// would with no cap at all, which is what keeps the 3.x-compatible listings
    /// (<c>GetJobKeys</c> and its neighbours) working through <c>HttpScheduler</c>: they ask for
    /// everything whether the answer is three rows or three million.
    /// </para>
    /// <para>
    /// Set it to <c>0</c> where an export or a migration really has to take everything in one call, and
    /// put the API behind something that says who may.
    /// </para>
    /// </remarks>
    public int MaxPageSize { get; set; } = DefaultMaxPageSize;

    /// <summary>
    /// The default <see cref="MaxPageSize" />, which is <c>EndpointHelper.MaxKeysToFetch</c>: one request
    /// asking for a thousand things is one limit, whichever endpoint it asks on.
    /// </summary>
    internal const int DefaultMaxPageSize = 1000;

    /// <summary>
    /// The authorization policy every route that names a scheduler is held to, evaluated against a
    /// <see cref="SchedulerResource" /> carrying that name. Null — the default — leaves the API as it was:
    /// whatever <c>RequireAuthorization(…)</c> the application put on the mapped group, applied uniformly
    /// to every scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set it and each request is checked with
    /// <c>IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)</c> before the
    /// scheduler is looked up, so a caller who fails cannot tell "no such scheduler" from "not yours": a
    /// refusal is <c>403</c> with problem details, and a <c>404</c> only ever answers a scheduler the
    /// caller was allowed to ask about. The scheduler listing is filtered the same way.
    /// </para>
    /// <para>
    /// The check is authorization, never authentication: an anonymous caller gets whatever the policy
    /// says, which is a <c>403</c> when the policy refuses. Put <c>RequireAuthorization()</c> on the
    /// mapped group as well to have an anonymous caller challenged with a <c>401</c> first.
    /// </para>
    /// </remarks>
    public string? SchedulerAuthorizationPolicy { get; set; }

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');

    /// <summary>
    /// Whether a value is usable as the path the API is served under.
    /// </summary>
    /// <remarks>
    /// Shared with <c>MapQuartzHttpApi(pattern)</c>, so a pattern given at the map site is held to the
    /// same rule as one configured at registration — the options validator has already run by then.
    /// </remarks>
    internal static bool IsRoutableApiPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith('/');
}

/// <summary>
/// Validates <see cref="QuartzHttpApiOptions"/>.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}"/> rather than an <c>AddOptions().Validate(lambda)</c>, so
/// every Quartz configuration mistake produces one exception type from one place — see the core
/// validators in <c>Quartz.Configuration</c>.
/// </remarks>
internal sealed class QuartzHttpApiOptionsValidator : IValidateOptions<QuartzHttpApiOptions>
{
    private readonly IServiceProviderIsService? registrations;

    /// <param name="registrations">
    /// What the container has, used to catch a per-scheduler policy with nothing to evaluate it. The
    /// default DI container supplies this; a third-party one that does not leaves the check unmade, which
    /// is why the parameter has a default rather than being required.
    /// </param>
    public QuartzHttpApiOptionsValidator(IServiceProviderIsService? registrations = null)
    {
        this.registrations = registrations;
    }

    public ValidateOptionsResult Validate(string? name, QuartzHttpApiOptions options)
    {
        if (!QuartzHttpApiOptions.IsRoutableApiPath(options.ApiPath))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHttpApiOptions.ApiPath)} is required and must start with '/', was '{options.ApiPath}'.");
        }

        if (options.MaxPageSize < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHttpApiOptions.MaxPageSize)} must not be negative, was {options.MaxPageSize}. Use 0 to leave paged requests unbounded.");
        }

        // A policy name with no IAuthorizationService behind it is a security setting that silently does
        // nothing, which is worse than one that is missing: say so at startup rather than at the first
        // request that would have been refused.
        if (!string.IsNullOrWhiteSpace(options.SchedulerAuthorizationPolicy)
            && registrations?.IsService(typeof(IAuthorizationService)) == false)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHttpApiOptions.SchedulerAuthorizationPolicy)} is '{options.SchedulerAuthorizationPolicy}', "
                + "but the container has no authorization services to evaluate it with - call services.AddAuthorization() and register that policy.");
        }

        return ValidateOptionsResult.Success;
    }
}