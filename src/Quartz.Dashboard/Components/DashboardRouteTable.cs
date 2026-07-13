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

using System.Reflection;

using Microsoft.AspNetCore.Components;

namespace Quartz.Dashboard.Components;

/// <summary>
/// Matches dashboard URLs against the route templates declared by the dashboard page components.
/// The standalone dashboard root uses this instead of the built-in Blazor router so the dashboard
/// can be served from a configurable <see cref="QuartzDashboardOptions.DashboardPath"/>; the
/// built-in router can only match the compile-time "/quartz" templates against the browser URL.
/// </summary>
internal static class DashboardRouteTable
{
    private static readonly Lazy<List<RouteEntry>> Routes = new(BuildRouteTable);

    /// <summary>
    /// Matches like <see cref="Match(string)"/> but retries with the dashboard path prefix
    /// stripped when the direct match fails. The relative path form is ambiguous when the
    /// application path base happens to end with the custom dashboard path: static server-side
    /// rendering then produces a dashboard-prefixed relative path that the base URI shape check
    /// in <see cref="DashboardLink.ToDashboardRelativePath"/> cannot distinguish from an
    /// interactive leaf, so a failed match gets a second chance without the prefix.
    /// </summary>
    internal static RouteData? Match(string dashboardRelativePath, QuartzDashboardOptions options)
    {
        return Match(ResolveLeaf(dashboardRelativePath, options));
    }

    /// <summary>
    /// Resolves a dashboard-relative path to the leaf that actually maps to a dashboard page,
    /// applying the same prefix-strip retry as <see cref="Match(string, QuartzDashboardOptions)"/>.
    /// Used where the leaf itself is needed (navigation highlighting) rather than the route data.
    /// Returns the direct form when neither the direct nor the stripped path maps to a page.
    /// </summary>
    internal static string ResolveLeaf(string dashboardRelativePath, QuartzDashboardOptions options)
    {
        string normalized = DashboardLink.NormalizeRelativePath(dashboardRelativePath);
        if (!options.HasCustomDashboardPath || Match(normalized) is not null)
        {
            return normalized;
        }

        if (DashboardLink.TryStripDashboardPrefix(normalized, options, out string stripped) && Match(stripped) is not null)
        {
            return stripped;
        }

        return normalized;
    }

    /// <summary>
    /// Matches a dashboard-root-relative path ("" for the dashboard root, no leading slash, as
    /// produced by <see cref="DashboardLink.ToDashboardRelativePath"/>) against the dashboard
    /// route table. Returns <see langword="null"/> when the path does not map to a dashboard page.
    /// </summary>
    internal static RouteData? Match(string dashboardRelativePath)
    {
        dashboardRelativePath = DashboardLink.NormalizeRelativePath(dashboardRelativePath);

        string[] segments = dashboardRelativePath.Length == 0 ? [] : dashboardRelativePath.Split('/');

        foreach (RouteEntry route in Routes.Value)
        {
            if (TryMatch(route, segments, out IReadOnlyDictionary<string, object?>? routeValues))
            {
                return new RouteData(route.PageType, routeValues);
            }
        }

        return null;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyRouteValues =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    private static bool TryMatch(RouteEntry route, string[] segments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? routeValues)
    {
        routeValues = null;

        if (route.Segments.Length != segments.Length)
        {
            return false;
        }

        // Allocate the value dictionary only once a parameter segment is reached, so
        // non-matching literal routes of the same length don't allocate at all
        Dictionary<string, object?>? values = null;
        for (int i = 0; i < segments.Length; i++)
        {
            string templateSegment = route.Segments[i];
            if (templateSegment.Length > 1 && templateSegment[0] == '{' && templateSegment[^1] == '}')
            {
                string parameterName = templateSegment[1..^1];
                values ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                values[parameterName] = Uri.UnescapeDataString(segments[i]);
            }
            else if (!string.Equals(templateSegment, segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        routeValues = values ?? EmptyRouteValues;
        return true;
    }

    private static List<RouteEntry> BuildRouteTable()
    {
        List<RouteEntry> routes = [];
        foreach (Type type in typeof(DashboardRouteTable).Assembly.GetTypes())
        {
            if (!typeof(IComponent).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (RouteAttribute attribute in type.GetCustomAttributes<RouteAttribute>())
            {
                string template = attribute.Template;
                if (!template.StartsWith(QuartzDashboardOptions.DefaultDashboardPath, StringComparison.OrdinalIgnoreCase)
                    || (template.Length > QuartzDashboardOptions.DefaultDashboardPath.Length && template[QuartzDashboardOptions.DefaultDashboardPath.Length] != '/'))
                {
                    // Fail fast: such a page would get a server-side endpoint from MapRazorComponents
                    // but could never be matched here, silently breaking interactive navigation to it
                    throw new InvalidOperationException(
                        $"Dashboard page component '{type.FullName}' declares route template '{template}' which is not rooted at " +
                        $"'{QuartzDashboardOptions.DefaultDashboardPath}'. All dashboard page templates must start with '{QuartzDashboardOptions.DefaultDashboardPath}'.");
                }

                string remainder = template.Substring(QuartzDashboardOptions.DefaultDashboardPath.Length);
                string[] segments = remainder.Length == 0 ? [] : remainder.TrimStart('/').Split('/');
                routes.Add(new RouteEntry(type, segments));
            }
        }

        // Deterministic precedence: shorter routes first, literal segments before parameters
        routes.Sort(static (left, right) =>
        {
            int byLength = left.Segments.Length.CompareTo(right.Segments.Length);
            return byLength != 0 ? byLength : CountParameters(left).CompareTo(CountParameters(right));
        });

        return routes;

        static int CountParameters(RouteEntry route)
        {
            int count = 0;
            foreach (string segment in route.Segments)
            {
                if (segment.Length > 0 && segment[0] == '{')
                {
                    count++;
                }
            }

            return count;
        }
    }

    private sealed record RouteEntry(Type PageType, string[] Segments);
}
