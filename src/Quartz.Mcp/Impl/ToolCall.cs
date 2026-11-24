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

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Mcp.Impl;

/// <summary>
/// One call of a tool, once its caller has been authorized and its key resolved: what the tool runs against, and
/// what it says back about a change for the record.
/// </summary>
internal sealed class ToolCall
{
    public required QuartzTool Tool { get; init; }

    public required McpCaller Caller { get; init; }

    public required McpArguments Arguments { get; init; }

    public required IServiceProvider Services { get; init; }

    public required TimeProvider Clock { get; init; }

    /// <summary>The key the call named, in the target's own spelling; <see langword="null" /> for <c>list_schedulers</c>.</summary>
    public SchedulerRef? Key { get; init; }

    /// <summary>What the key resolved to; <see langword="null" /> for <c>list_schedulers</c>.</summary>
    public SchedulerResolution? Resolution { get; init; }

    /// <summary>The scheduler the call runs against.</summary>
    public IScheduler Scheduler => Resolution?.Scheduler ?? throw new InvalidOperationException($"{Tool.Name} names no scheduler.");

    /// <summary>Whether the call is a confirmed tool's preview, which changes nothing and is neither counted nor recorded.</summary>
    public bool IsPreview { get; init; }

    /// <summary>What a change was made to — <c>group.name</c> — for its record; the scheduler's key when unset.</summary>
    public string? Subject { get; set; }

    /// <summary>Whether a change was applied: <see langword="false" /> for one the scheduler answered as not done.</summary>
    public bool Applied { get; set; } = true;

    /// <summary>What the record says beside the change: a pause's reason.</summary>
    public string? Note { get; set; }

    /// <summary>The time an answer is as of: the clock when the call's reads completed.</summary>
    public DateTimeOffset Now() => Clock.GetUtcNow();

    /// <summary>
    /// Where the scheduler is and how fresh what is known of it is: an agent's last heartbeat, a window's live
    /// nodes. A scheduler of this process and an HTTP target answered live, and carry neither.
    /// </summary>
    public async ValueTask<SchedulerFreshness> Freshness(CancellationToken cancellationToken)
    {
        SchedulerResolution resolution = Resolution ?? throw new InvalidOperationException($"{Tool.Name} names no scheduler.");
        string key = resolution.Ref.Key;

        if (resolution.IsWindow)
        {
            try
            {
                WindowLiveness liveness = await WindowLiveness.Read(resolution.Scheduler, cancellationToken).ConfigureAwait(false);
                return new SchedulerFreshness(key, SchedulerOrigin.Window) { LiveNodes = liveness.LiveNodes, Nodes = liveness.Nodes };
            }
            catch (SchedulerException)
            {
                return new SchedulerFreshness(key, SchedulerOrigin.Window);
            }
        }

        if (resolution.Scheduler is IProxyScheduler proxy)
        {
            DateTimeOffset? lastSeen = proxy.Target is { } target
                ? Services.GetService<SchedulerTargets>()?.Find(target)?.Liveness?.Invoke().LastSeenUtc
                : null;
            return new SchedulerFreshness(key, proxy.Origin) { LastSeenUtc = lastSeen };
        }

        return new SchedulerFreshness(key, SchedulerOrigin.Container);
    }

    /// <summary>
    /// The store holding the scheduler's execution history, by the HTTP API's rule.
    /// </summary>
    /// <exception cref="McpToolException">Its target or window keeps no history this process can read.</exception>
    public IExecutionHistoryStore History()
    {
        IExecutionHistoryStore shared = Services.GetRequiredService<IExecutionHistoryStore>();
        return ExecutionHistoryLookup.ForResolved(Services, shared, Scheduler, targeted: Key?.Target is not null, out string? refusal)
               ?? throw new McpToolException(McpErrorCodes.NotServed, refusal!);
    }
}

/// <summary>
/// A call refused or failed in the endpoint's own terms, which the invoker answers as a tool error.
/// </summary>
internal sealed class McpToolException : Exception
{
    public McpToolException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// The error codes a tool error carries, which an agent can branch on.
/// </summary>
internal static class McpErrorCodes
{
    public const string InvalidArguments = "invalid_arguments";
    public const string ReadOnly = "read_only";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string NotServed = "not_served";
    public const string RateLimited = "rate_limited";
    public const string ConfirmationRefused = "confirmation_refused";
    public const string TargetFault = "target_fault";
    public const string SchedulerError = "scheduler_error";
}
