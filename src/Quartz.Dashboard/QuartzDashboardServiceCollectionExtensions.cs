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

using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;

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
                "DashboardPath must be a simple URL path: it cannot contain '{', '}', '?', '#', '.' or '..' segments, or empty segments ('//')");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // The hub's scheduler status goes out as its name, the way Quartz writes it on every other wire:
        // a browser rendering a live event should read "Standby" rather than a number whose meaning
        // depends on the build. Named per enum rather than a blanket converter, because these options
        // belong to the whole application's SignalR and a host's own hubs must keep their own format.
        services.AddSignalR()
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter<SchedulerStatus>()));
        services.AddHttpContextAccessor();

        // The dashboard reads the schedulers in its own process: every page goes through the in-process
        // client, which hands the pages triggers, calendars and job data maps as themselves. TryAdd, so an
        // application that registers its own IQuartzApiClient first is the one that answers.
        services.TryAddScoped<IQuartzApiClient>(static provider => new InProcessQuartzApiClient(
            provider.GetRequiredService<ISchedulerRepository>(),
            provider.GetRequiredService<ISchedulerRegistry>(),
            provider.GetRequiredService<IOptions<QuartzDashboardOptions>>(),
            provider.GetRequiredService<IDashboardHistoryStore>()));
        services.TryAddScoped<SchedulerState>();
        services.TryAddScoped<ToastService>();
        services.TryAddSingleton<IDashboardLiveConnectionFactory, SignalRDashboardLiveConnectionFactory>();
        services.TryAddSingleton<IDashboardHistoryStore, DashboardHistoryStore>();
        services.TryAddSingleton<DashboardActionLogService>();

        // The dashboard's own plugins, registered rather than named by a quartz.plugin.*.type key. A type
        // name in a property bag is how a plugin is configured from a file; a package that knows its own
        // plugin types has no reason to spell them as strings and have them loaded back by reflection.
        //
        // Added to every scheduler in the container rather than to the default one. The dashboard renders
        // whatever schedulers the container holds, so a plugin that only reached the unkeyed registration
        // left a scheduler registered with AddQuartz(name, …) rendering pages whose live view and history
        // were always empty, with nothing to say why. Each scheduler gets its own instance, initialized
        // with its own name, which is what the plugins broadcast and record under. The names are the short
        // ones these plugins have always been configured with: a plugin is told its name when it is
        // initialized, and one told a different name would key its history rows differently.
        services.ConfigureAllQuartzSchedulers(static quartz =>
        {
            quartz.AddPlugin<DashboardLiveEventsPlugin>("quartzDashboardLiveEvents");
            quartz.AddPlugin<DashboardHistoryPlugin>("quartzDashboardHistory");
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
    internal static bool IsRoutableDashboardPath(string? path)
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
