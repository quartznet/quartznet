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

namespace Quartz;

/// <summary>
/// How the dashboard is served and what it is allowed to do.
/// </summary>
/// <remarks>
/// There is nothing here that points the dashboard at a scheduler: it renders the schedulers in its own
/// process, through the <c>IQuartzApiClient</c> registered in the container.
/// </remarks>
public sealed class QuartzDashboardOptions
{
    internal const string DefaultDashboardPath = "/quartz";

    /// <summary>
    /// The base path the dashboard UI is served from. Defaults to "/quartz".
    /// A custom value is honored when the dashboard hosts its own Blazor root
    /// (the parameterless <c>MapQuartzDashboard()</c> overload). When integrating into an
    /// existing Blazor application the dashboard page routes are fixed at "/quartz".
    /// </summary>
    /// <remarks>
    /// <c>MapQuartzDashboard(pattern)</c> says the same thing where the endpoints are mapped, which is
    /// where the rest of an application's routes are written, and a pattern given there wins over this.
    /// </remarks>
    public string DashboardPath { get; set; } = DefaultDashboardPath;

    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// The authorization policy each scheduler is held to, evaluated against a
    /// <see cref="SchedulerResource" /> carrying that scheduler's name. Null — the default — leaves the
    /// dashboard as it was: whoever passes <see cref="AuthorizationPolicy" /> sees every scheduler in the
    /// process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set it and the scheduler picker offers only the schedulers the visitor passes for, a page opened on
    /// one they do not says so without reading anything, and the live-events hub refuses to subscribe them
    /// to it. The two policies compose: <see cref="AuthorizationPolicy" /> decides who reaches the
    /// dashboard at all, this one decides which schedulers they see once they are in, and
    /// <see cref="ReadOnly" /> still decides what anyone may change.
    /// </para>
    /// <para>
    /// It is the same policy and the same resource the HTTP API's
    /// <c>QuartzHttpApiOptions.SchedulerAuthorizationPolicy</c> evaluates, so one
    /// <c>AuthorizationHandler&lt;TRequirement, SchedulerResource&gt;</c> answers for both surfaces.
    /// </para>
    /// </remarks>
    public string? SchedulerAuthorizationPolicy { get; set; }

    public bool ReadOnly { get; set; }

    /// <summary>
    /// How far back the dashboard's own history store keeps executions and misfires. Defaults to 24 hours.
    /// </summary>
    /// <remarks>
    /// The count bound below cannot answer for a scheduler that has gone quiet: it keeps whatever it last
    /// recorded, so a page shows executions from an arbitrary distance in the past with nothing to say how
    /// old they are. Both bounds apply, and whichever bites first wins.
    /// </remarks>
    public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How many executions and how many misfires the dashboard's own history store keeps per scheduler,
    /// oldest dropped first. Defaults to 2000 of each.
    /// </summary>
    public int HistoryMaxEntriesPerScheduler { get; set; } = 2000;

    /// <summary>
    /// <see cref="DashboardPath"/> normalized to a rooted path without a trailing slash,
    /// falling back to <see cref="DefaultDashboardPath"/> when unset or empty.
    /// </summary>
    internal string TrimmedDashboardPath => DashboardPathCache.Trimmed;

    /// <summary>
    /// Whether <see cref="DashboardPath"/> differs from the compile-time default "/quartz".
    /// A custom path implies the standalone hosting mode because it is rejected when
    /// integrating with an existing Blazor application.
    /// </summary>
    internal bool HasCustomDashboardPath => DashboardPathCache.HasCustom;

    /// <summary>
    /// <see cref="TrimmedDashboardPath"/> in its percent-encoded form, as browsers emit it in
    /// request URIs and the &lt;base href&gt;. Server-side route patterns keep the raw form
    /// (route matching compares decoded values); client-side URI comparisons need this one.
    /// </summary>
    internal string EscapedDashboardPath => DashboardPathCache.Escaped;

    private DerivedDashboardPath? dashboardPathCache;

    /// <summary>
    /// Values derived from <see cref="DashboardPath"/>, computed once and reused — they are read
    /// on Blazor render hot paths (links, route matching) while the option itself only changes
    /// during startup configuration. Held behind a single reference so a concurrent reader always
    /// observes a fully-populated instance (reference reads/writes are atomic) even if the option
    /// is mutated mid-render.
    /// </summary>
    private DerivedDashboardPath DashboardPathCache
    {
        get
        {
            string source = DashboardPath;
            DerivedDashboardPath? cache = dashboardPathCache;
            if (cache is null || !string.Equals(cache.Source, source, StringComparison.Ordinal))
            {
                string trimmed = DefaultDashboardPath;
                if (!string.IsNullOrWhiteSpace(source))
                {
                    string candidate = source.Trim().Trim('/');
                    if (candidate.Length > 0)
                    {
                        trimmed = "/" + candidate;
                    }
                }

                string escaped = new Uri("http://localhost" + trimmed).AbsolutePath;
                bool hasCustom = !string.Equals(trimmed, DefaultDashboardPath, StringComparison.OrdinalIgnoreCase);
                cache = new DerivedDashboardPath(source, trimmed, escaped, hasCustom);
                dashboardPathCache = cache;
            }

            return cache;
        }
    }

    private sealed record DerivedDashboardPath(string Source, string Trimmed, string Escaped, bool HasCustom);
}
