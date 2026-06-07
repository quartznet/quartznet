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
    /// The base URL used by the HTTP API client to construct request URIs.
    /// When set, this is used instead of deriving the base URL from the incoming HTTP request.
    /// This should be set to the root URL of the host application (e.g., "https://myapp.example.com/").
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// <see cref="DashboardPath"/> normalized to a rooted path without a trailing slash,
    /// falling back to <see cref="DefaultDashboardPath"/> when unset or empty.
    /// </summary>
    internal string TrimmedDashboardPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DashboardPath))
            {
                return DefaultDashboardPath;
            }

            string trimmed = DashboardPath.Trim().Trim('/');
            return trimmed.Length == 0 ? DefaultDashboardPath : "/" + trimmed;
        }
    }

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');
}
