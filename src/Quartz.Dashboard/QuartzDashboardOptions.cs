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

public class QuartzDashboardOptions
{
    internal const string DefaultDashboardPath = "/quartz";

    /// <summary>
    /// The base path the dashboard UI is served from. Defaults to "/quartz".
    /// A custom value is honored when the dashboard hosts its own Blazor root
    /// (the parameterless <c>MapQuartzDashboard()</c> overload). When integrating into an
    /// existing Blazor application the dashboard page routes are fixed at "/quartz".
    /// </summary>
    public string DashboardPath { get; set; } = DefaultDashboardPath;

    public string? AuthorizationPolicy { get; set; }

    public IDashboardAuthorizationFilter? AuthorizationFilter { get; set; }

    public bool ReadOnly { get; set; }

    public string ApiPath { get; set; } = "/quartz-api";

    /// <summary>
    /// <see cref="DashboardPath"/> normalized to a rooted path without a trailing slash,
    /// falling back to <see cref="DefaultDashboardPath"/> when unset or empty.
    /// </summary>
    internal string TrimmedDashboardPath => DashboardPathCache.Trimmed;

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');

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

    private (string Source, string Trimmed, string Escaped, bool HasCustom)? dashboardPathCache;

    /// <summary>
    /// Values derived from <see cref="DashboardPath"/>, computed once and reused — they are read
    /// on Blazor render hot paths (links, route matching) while the option itself only changes
    /// during startup configuration.
    /// </summary>
    private (string Source, string Trimmed, string Escaped, bool HasCustom) DashboardPathCache
    {
        get
        {
            string source = DashboardPath;
            var cache = dashboardPathCache;
            if (cache is null || !string.Equals(cache.Value.Source, source, StringComparison.Ordinal))
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
                cache = (source, trimmed, escaped, hasCustom);
                dashboardPathCache = cache;
            }

            return cache.Value;
        }
    }
}
