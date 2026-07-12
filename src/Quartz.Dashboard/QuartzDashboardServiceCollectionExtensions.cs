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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Util;

namespace Quartz;

public static class QuartzDashboardServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzDashboard(
        this IServiceCollection services,
        Action<QuartzDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<QuartzDashboardOptions> optionsBuilder = services
            .AddOptions<QuartzDashboardOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DashboardPath) && options.DashboardPath.StartsWith('/'),
                "DashboardPath must start with '/'")
            .Validate(
                options => IsRoutableDashboardPath(options.DashboardPath),
                "DashboardPath must be a simple URL path: it cannot contain '{', '}', '?', '#', '.' or '..' segments, or empty segments ('//')")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiPath) && options.ApiPath.StartsWith('/'),
                "ApiPath must start with '/'");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddSignalR();
        services.AddHttpContextAccessor();

        services.TryAddScoped<IQuartzApiClient, InProcessQuartzApiClient>();
        services.TryAddScoped<SchedulerState>();
        services.TryAddScoped<ToastService>();
        services.TryAddSingleton<IDashboardHistoryStore, DashboardHistoryStore>();
        services.TryAddSingleton<DashboardActionLogService>();

        services.Configure<QuartzOptions>(options =>
        {
            options["quartz.plugin.quartzDashboardLiveEvents.type"] = typeof(DashboardLiveEventsPlugin).AssemblyQualifiedNameWithoutVersion();
            options["quartz.plugin.quartzDashboardHistory.type"] = typeof(DashboardHistoryPlugin).AssemblyQualifiedNameWithoutVersion();
        });

        return services;
    }

    private static readonly char[] InvalidDashboardPathChars = ['{', '}', '?', '#'];

    /// <summary>
    /// Validates that <see cref="QuartzDashboardOptions.DashboardPath"/> is a plain URL path: the
    /// value is concatenated into route templates (where <c>{</c>/<c>}</c> would be parsed as route
    /// parameters) and percent-encoded for client-side comparisons (where <c>?</c>/<c>#</c> and
    /// <c>.</c>/<c>..</c> segments would be truncated or collapsed, diverging from the server route).
    /// </summary>
    private static bool IsRoutableDashboardPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            // the empty/whitespace case is reported by the "must start with '/'" validation
            return true;
        }

        string trimmed = path.Trim().Trim('/');
        if (trimmed.Length == 0)
        {
            // normalizes to the default "/quartz"
            return true;
        }

        foreach (string segment in trimmed.Split('/'))
        {
            if (segment.Length == 0
                || segment == "."
                || segment == ".."
                || segment.IndexOfAny(InvalidDashboardPathChars) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}
