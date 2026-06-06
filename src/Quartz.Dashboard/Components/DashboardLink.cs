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

namespace Quartz.Dashboard.Components;

/// <summary>
/// Builds dashboard link URLs relative to the application base URI so they honor the configured
/// <see cref="QuartzDashboardOptions.DashboardPath"/> and any path base set by the host or a
/// reverse proxy (relative URLs resolve against the rendered &lt;base href&gt;).
/// </summary>
internal static class DashboardLink
{
    internal static string To(QuartzDashboardOptions options, string subPath)
    {
        string root = options.TrimmedDashboardPath.TrimStart('/');
        return subPath.Length == 0 ? root : root + "/" + subPath;
    }

    /// <summary>
    /// Normalizes a base-relative path for matching and comparisons: strips the query string
    /// and fragment and trims surrounding slashes. Shared by route matching and the
    /// navigation menu so both normalize URLs identically.
    /// </summary>
    internal static string NormalizeRelativePath(string relativePath)
    {
        int delimiterIndex = relativePath.IndexOfAny(['?', '#']);
        if (delimiterIndex >= 0)
        {
            relativePath = relativePath.Substring(0, delimiterIndex);
        }

        return relativePath.Trim('/');
    }
}
