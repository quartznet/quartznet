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
        string? relative = ToBaseRelative(uri, baseUri);
        if (relative is null)
        {
            return null;
        }

        relative = NormalizeRelativePath(relative);

        // Decide which shape the base URI has before touching the relative path. On the interactive
        // circuit in custom-path mode the base URI is the rendered dashboard-rooted <base href>, so
        // the base-relative path is already dashboard-rooted and must not be prefix-stripped — a
        // dashboard path whose name collides with a page route (e.g. "/jobs") would otherwise be
        // misresolved. Comparisons use the percent-encoded form because browsers emit encoded URIs.
        // The leading slash of the dashboard path keeps the EndsWith comparison segment-aligned.
        string dashboardPath = options.EscapedDashboardPath;
        if (options.HasCustomDashboardPath
            && baseUri.TrimEnd('/').EndsWith(dashboardPath, StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        // Otherwise the base URI is the application path base (static server-side rendering; also
        // the only shape used in default-path mode) and the base-relative path carries the dashboard
        // path prefix; strip it. A path-base-rooted URI without the prefix (e.g. the application
        // root itself) is not a dashboard location.
        string dashboardRoot = dashboardPath.TrimStart('/');
        if (relative.Equals(dashboardRoot, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (relative.StartsWith(dashboardRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return relative.Substring(dashboardRoot.Length + 1);
        }

        return null;
    }

    /// <summary>
    /// Returns the base-relative part of an absolute URI, or <see langword="null"/> when the URI
    /// lies outside the base URI space.
    /// </summary>
    private static string? ToBaseRelative(string uri, string baseUri)
    {
        if (uri.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase))
        {
            return uri.Substring(baseUri.Length);
        }

        // Mirror NavigationManager.ToBaseRelativePath's special case: the document URL may equal
        // the base URI without its trailing slash (e.g. "/my-api/quartz" with base "/my-api/quartz/")
        if (string.Concat(StripQueryAndFragment(uri), "/").Equals(baseUri, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
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
        return StripQueryAndFragment(relativePath).Trim('/');
    }

    private static string StripQueryAndFragment(string uriOrPath)
    {
        int delimiterIndex = uriOrPath.IndexOfAny(['?', '#']);
        return delimiterIndex >= 0 ? uriOrPath.Substring(0, delimiterIndex) : uriOrPath;
    }
}
