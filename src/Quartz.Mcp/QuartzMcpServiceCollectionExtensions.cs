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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Quartz.AspNetCore;
using Quartz.Mcp.Impl;

namespace Quartz;

/// <summary>
/// Registers the Quartz MCP server: the tools an AI agent reads and operates this process's schedulers, and its
/// fleet's, through. Added in 4.9.
/// </summary>
public static class QuartzMcpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Quartz MCP server, served over Streamable HTTP once <c>MapQuartzMcp()</c> maps it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The endpoint is read-only by default and stateless always: every request stands alone, so nothing binds a
    /// session to a caller and nothing needs affinity between instances. It serves every scheduler of the
    /// container and those behind its targets, named by key as the dashboard names them.
    /// </para>
    /// <para>
    /// It is container-wide, as the HTTP API is, and holds the container's one MCP server: an application with MCP
    /// tools of its own cannot add Quartz's to them in 4.9, and <c>MapQuartzMcp()</c> refuses to start rather than
    /// serve the two together.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">What the endpoint allows: see <see cref="QuartzMcpOptions" />.</param>
    public static IServiceCollection AddQuartzMcp(this IServiceCollection services, Action<QuartzMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<QuartzMcpOptions>, QuartzMcpOptionsValidator>());
        OptionsBuilder<QuartzMcpOptions> options = services.AddOptions<QuartzMcpOptions>().ValidateOnStart();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        // Once: a second call configures the options again and registers nothing twice.
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(McpToolInvoker)))
        {
            return services;
        }

        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<McpAccess>();
        services.TryAddSingleton<McpRecords>();
        services.TryAddSingleton<ChangeRateLimiter>();
        services.TryAddSingleton<ConfirmationTokens>();
        services.TryAddSingleton<McpToolInvoker>();

        // The history tools read what the schedulers have run, so something has to record it, as AddQuartzHttpApi
        // says for the API's history routes. Idempotent.
        services.AddQuartzExecutionHistory();

        // Refuses to start an application whose mapped endpoint nothing authorizes, as the HTTP API does.
        services.TryAddSingleton<QuartzMappedEndpoints>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QuartzEndpointAuthorizationGuard>());

        foreach (QuartzTool tool in QuartzTools.Create())
        {
            services.AddSingleton<McpServerTool>(tool);
        }

        services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new Implementation
                {
                    Name = "Quartz.NET",
                    Title = "Quartz.NET schedulers",
                    Version = typeof(QuartzMcpOptions).Assembly.GetName().Version?.ToString(3) ?? "4",
                };
                server.ServerInstructions =
                    "Reads and operates Quartz.NET schedulers. Start with list_schedulers, and name a scheduler by the key it gives. "
                    + "Changes are refused unless this endpoint allows them and your role does; reschedule and unschedule answer a preview "
                    + "and a confirmation token first, which you confirm only after your user agrees. "
                    + QuartzTool.UntrustedText;
            })
            .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateless)
            .WithRequestFilters(filters => filters.AddListToolsFilter(QuartzTools.Filter));

        return services;
    }
}
