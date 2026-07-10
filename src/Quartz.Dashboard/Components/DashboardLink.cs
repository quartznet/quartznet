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
        if (options.HasCustomDashboardPath)
        {
            // QuartzDashboardApp renders a dashboard-rooted <base href> in custom-path mode (a custom
            // path implies standalone hosting), so links are relative to the dashboard root itself;
            // an empty href resolves to the base URI (RFC 3986 §5.4)
            return subPath;
        }

        string root = options.TrimmedDashboardPath.TrimStart('/');
        return subPath.Length == 0 ? root : root + "/" + subPath;
    }

    /// <summary>
    /// Converts an absolute URI into a path relative to the dashboard root ("" for the root itself,
    /// no leading slash, query string and fragment stripped), or <see langword="null"/> when the URI
    /// is not a dashboard location. Handles both base URI shapes seen in standalone custom-path mode:
    /// during static server-side rendering the base URI is the application path base and the relative
    /// path still carries the dashboard path prefix, while on the interactive circuit the base URI is
    /// the dashboard root itself (the rendered &lt;base href&gt;).
    /// </summary>
    internal static string? ToDashboardRelativePath(string uri, string baseUri, QuartzDashboardOptions options)
    {
        string relative;
        if (uri.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase))
        {
            relative = NormalizeRelativePath(uri.Substring(baseUri.Length));
        }
        else
        {
            // Mirror NavigationManager.ToBaseRelativePath's special case: the document URL may equal
            // the base URI without its trailing slash (e.g. "/my-api/quartz" with base "/my-api/quartz/")
            int delimiterIndex = uri.IndexOfAny(['?', '#']);
            string uriWithoutSuffix = delimiterIndex >= 0 ? uri.Substring(0, delimiterIndex) : uri;
            if (string.Concat(uriWithoutSuffix, "/").Equals(baseUri, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return null;
        }

        // During static server-side rendering the base URI is the application path base, so the
        // base-relative path still carries the dashboard path prefix; strip it. This is also the
        // only shape used in default-path mode. Checked first so a pathological path base ending
        // in the dashboard path still resolves deterministically.
        string dashboardRoot = options.TrimmedDashboardPath.TrimStart('/');
        if (relative.Equals(dashboardRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (relative.StartsWith(dashboardRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return relative.Substring(dashboardRoot.Length + 1);
        }

        // In custom-path mode the interactive circuit's base URI is the rendered dashboard-rooted
        // <base href>, so the base-relative path is already dashboard-rooted. The leading slash of
        // TrimmedDashboardPath keeps the EndsWith comparison segment-aligned.
        if (options.HasCustomDashboardPath
            && new Uri(baseUri).AbsolutePath.TrimEnd('/').EndsWith(options.TrimmedDashboardPath, StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        return null;
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
