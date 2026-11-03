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

using Microsoft.Extensions.Configuration;

namespace Quartz.Impl;

/// <summary>
/// Reads <see cref="DashboardAgentOptions" /> out of the application's configuration: the section
/// <c>Quartz:DashboardAgent</c>, or a named scheduler's own <c>Quartz:Schedulers:{name}:DashboardAgent</c>.
/// </summary>
/// <remarks>
/// <para>
/// The keys are the options' own names, and only the ones configuration can carry: a delegate has no
/// spelling in a settings file. They are bound into <see cref="DashboardAgentSettings" /> rather than onto
/// the options, so the binding generator has nothing it cannot bind in front of it, and so a key that is
/// absent leaves whatever the options already held.
/// </para>
/// <para>
/// A named scheduler reads its own section when there is one and the shared section when there is not,
/// the rule <c>AddQuartz(name, configuration)</c> resolves a scheduler's settings by. That is what lets an
/// AppHost write <c>Quartz__DashboardAgent__Endpoint</c> once for a worker whose one scheduler happens to be
/// named, and <c>Quartz__Schedulers__billing__DashboardAgent__Target</c> for a worker with two.
/// </para>
/// </remarks>
internal static class DashboardAgentConfiguration
{
    /// <summary>
    /// The section, under <c>Quartz</c> or under a scheduler's own section, that holds the agent's settings.
    /// </summary>
    internal const string SectionName = "DashboardAgent";

    /// <summary>
    /// The section a scheduler's agent reads: its own when it is named and has one, the shared one otherwise.
    /// </summary>
    internal static IConfigurationSection Section(IConfiguration configuration, string schedulerName)
    {
        IConfigurationSection quartz = configuration.GetSection("Quartz");

        if (!string.IsNullOrEmpty(schedulerName))
        {
            IConfigurationSection own = quartz.GetSection("Schedulers").GetSection(schedulerName).GetSection(SectionName);
            if (own.Exists())
            {
                return own;
            }
        }

        return quartz.GetSection(SectionName);
    }

    /// <summary>
    /// Applies what <paramref name="configuration" /> says about this scheduler's agent to
    /// <paramref name="options" />. A container without configuration — one built from a bare
    /// <c>ServiceCollection</c> — leaves the options as they are.
    /// </summary>
    internal static void Apply(DashboardAgentOptions options, IConfiguration? configuration, string schedulerName)
    {
        if (configuration is null)
        {
            return;
        }

        IConfigurationSection section = Section(configuration, schedulerName);
        if (!section.Exists())
        {
            return;
        }

        DashboardAgentSettings settings = new();
        section.Bind(settings);
        settings.CopyTo(options);
    }
}

/// <summary>
/// The settings of <see cref="DashboardAgentOptions" /> that configuration can carry, each nullable so an
/// absent key is told apart from a default.
/// </summary>
internal sealed class DashboardAgentSettings
{
    public Uri? Endpoint { get; set; }

    public string? Token { get; set; }

    public string? Target { get; set; }

    public bool? ReadOnly { get; set; }

    public SchedulerAccess? MaxAccess { get; set; }

    public TimeSpan? HeartbeatInterval { get; set; }

    public int? MaxConcurrentOperations { get; set; }

    public int? MaxPageSize { get; set; }

    public void CopyTo(DashboardAgentOptions options)
    {
        options.Endpoint = Endpoint ?? options.Endpoint;
        options.Token = Token ?? options.Token;
        options.Target = Target ?? options.Target;
        options.ReadOnly = ReadOnly ?? options.ReadOnly;
        options.MaxAccess = MaxAccess ?? options.MaxAccess;
        options.HeartbeatInterval = HeartbeatInterval ?? options.HeartbeatInterval;
        options.MaxConcurrentOperations = MaxConcurrentOperations ?? options.MaxConcurrentOperations;
        options.MaxPageSize = MaxPageSize ?? options.MaxPageSize;
    }
}
