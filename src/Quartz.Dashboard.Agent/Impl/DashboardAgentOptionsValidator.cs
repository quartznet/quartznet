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

using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// Validates <see cref="DashboardAgentOptions" />.
/// </summary>
/// <remarks>
/// An <see cref="IValidateOptions{TOptions}" /> rather than an <c>AddOptions().Validate(lambda)</c>, so
/// every Quartz configuration mistake produces one exception type from one place — see the core
/// validators in <c>Quartz.Configuration</c>. Run again by the plugin when it initializes, so a scheduler
/// whose options were never resolved through <c>IOptions</c> is still refused with the same words.
/// </remarks>
internal sealed class DashboardAgentOptionsValidator : IValidateOptions<DashboardAgentOptions>
{
    public ValidateOptionsResult Validate(string? name, DashboardAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Endpoint is null || !options.Endpoint.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DashboardAgentOptions.Endpoint)} is required and must be an absolute URI naming the dashboard's agent hub, "
                + "such as https://ops.example.com/quartz/agents.");
        }

        if (string.IsNullOrWhiteSpace(options.Token) && options.AccessTokenProvider is null)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DashboardAgentOptions.Token)} or {nameof(DashboardAgentOptions.AccessTokenProvider)} is required: "
                + "the dashboard accepts no agent it cannot authenticate.");
        }

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DashboardAgentOptions.HeartbeatInterval)} must be positive, was {options.HeartbeatInterval}.");
        }

        if (options.HeartbeatInterval > TimerLimits.MaxDelay)
        {
            return ValidateOptionsResult.Fail(TimerLimits.TooLong(
                nameof(DashboardAgentOptions.HeartbeatInterval),
                options.HeartbeatInterval,
                TimerLimits.MaxDelay,
                "The agent waits it out between heartbeats on a timer."));
        }

        if (options.MaxConcurrentOperations < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DashboardAgentOptions.MaxConcurrentOperations)} must be at least 1, was {options.MaxConcurrentOperations}.");
        }

        if (options.MaxPageSize < 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(DashboardAgentOptions.MaxPageSize)} must not be negative, was {options.MaxPageSize}. Use 0 to leave paged requests unbounded.");
        }

        if (options.Target is not null && SchedulerTargets.Problem(options.Target) is { } problem)
        {
            return ValidateOptionsResult.Fail($"{nameof(DashboardAgentOptions.Target)}: {problem}");
        }

        return ValidateOptionsResult.Success;
    }
}
