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
        services.AddHttpClient("QuartzDashboard");

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
}
