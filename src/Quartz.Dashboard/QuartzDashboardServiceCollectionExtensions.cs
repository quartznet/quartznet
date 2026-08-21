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

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Serialization.SystemTextJson;
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
                "DashboardPath must be a simple URL path: it cannot contain '{', '}', '?', '#', '.' or '..' segments, or empty segments ('//')")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiPath) && options.ApiPath.StartsWith('/'),
                "ApiPath must start with '/'")
            .Validate(
                // This is the address the dashboard calls its own API back on, so a relative one cannot
                // be a base address. Rejecting it here beats a UriFormatException from the first request.
                options => options.BaseUrl is null || options.BaseUrl.IsAbsoluteUri,
                "BaseUrl must be an absolute URL, for example https://myapp.example.com/");

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddSignalR();
        services.AddHttpContextAccessor();
        services.AddHttpClient("QuartzDashboard");

        // The dashboard renders every scheduler in the container, so it cannot read any single scheduler's
        // serializers; register a custom trigger or calendar serializer here to have the dashboard
        // understand it.
        services.TryAddSingleton<SystemTextJsonSerializerRegistry>();

        services.TryAddSingleton<DashboardSerializerOptions>();
        services.TryAddScoped<IQuartzApiClient>(static provider => new InProcessQuartzApiClient(
            provider.GetRequiredService<ISchedulerRepository>(),
            provider.GetRequiredService<IOptions<QuartzDashboardOptions>>(),
            provider.GetRequiredService<IDashboardHistoryStore>(),
            provider.GetRequiredService<DashboardSerializerOptions>().Deserializer));
        services.TryAddScoped<SchedulerState>();
        services.TryAddScoped<ToastService>();
        services.TryAddSingleton<IDashboardHistoryStore, DashboardHistoryStore>();
        services.TryAddSingleton<DashboardActionLogService>();

        // The dashboard's own plugins, registered rather than named by a quartz.plugin.*.type key. A type
        // name in a property bag is how a plugin is configured from a file; a package that knows its own
        // plugin types has no reason to spell them as strings and have them loaded back by reflection.
        var quartz = new QuartzBuilder(services, schedulerKey: null);
        AddDashboardPlugin<DashboardLiveEventsPlugin>(quartz, "quartzDashboardLiveEvents");
        AddDashboardPlugin<DashboardHistoryPlugin>(quartz, "quartzDashboardHistory");

        return services;
    }

    /// <summary>
    /// Adds one of the dashboard's plugins under the short name it has always been configured with.
    /// </summary>
    /// <remarks>
    /// The name is registered separately from the plugin because a plugin is told its name when it is
    /// initialized, and plugins that derive persisted job keys from that name would otherwise write a
    /// different set of rows than the same plugin named by a <c>quartz.plugin.&lt;name&gt;.*</c> key.
    /// </remarks>
    private static void AddDashboardPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        QuartzBuilder quartz,
        string name) where T : class, ISchedulerPlugin
    {
        quartz.Services.AddSingleton(new SchedulerPluginName(quartz.SchedulerName, typeof(T), name));
        quartz.AddPlugin<T>();
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
