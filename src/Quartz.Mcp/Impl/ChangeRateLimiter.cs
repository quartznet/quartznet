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

using System.Collections.Concurrent;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Options;

namespace Quartz.Mcp.Impl;

/// <summary>
/// <see cref="QuartzMcpOptions.MaxChangesPerMinute" />: a sliding window of a minute per caller, over every tool that
/// changes something — the changes made and the changes refused.
/// </summary>
/// <remarks>
/// <para>
/// Partitioned by the caller's digest, so one agent's loop does not starve another, and the identity is not kept.
/// Four segments, no queue: a change over the limit is refused at once with when to try again, never held. A
/// preview and a read are not counted. The count is this process's.
/// </para>
/// <para>
/// A change a policy refuses is counted too, so that a caller looping on one cannot fill the Action Log's 250
/// entries or the log: past the limit its refusals are still answered, and no longer recorded. The limit itself is
/// logged once per caller per window, not once per call.
/// </para>
/// </remarks>
internal sealed class ChangeRateLimiter : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<string>? limiter;
    private readonly TimeProvider timeProvider;

    /// <summary>Until when a caller's being over the limit has been reported, by caller digest.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> reportedUntil = new(StringComparer.Ordinal);

    public ChangeRateLimiter(IOptions<QuartzMcpOptions> options, TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;

        int permits = options.Value.MaxChangesPerMinute;
        if (permits <= 0)
        {
            return;
        }

        limiter = PartitionedRateLimiter.Create<string, string>(caller => RateLimitPartition.GetSlidingWindowLimiter(
            caller,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = Window,
                SegmentsPerWindow = 4,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    /// <summary>
    /// Counts one change, made or refused, for <paramref name="callerDigest" />: <see langword="false" /> when it is
    /// over the limit, with when to try again, and whether this is the first time in the window it is, which is when
    /// the limit is logged.
    /// </summary>
    public bool TryAcquire(string callerDigest, out int retryAfterSeconds, out bool report)
    {
        retryAfterSeconds = 0;
        report = false;
        if (limiter is null)
        {
            return true;
        }

        using RateLimitLease lease = limiter.AttemptAcquire(callerDigest);
        if (lease.IsAcquired)
        {
            return true;
        }

        TimeSpan retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan after) ? after : Window / 4;
        retryAfterSeconds = Math.Max(1, (int) Math.Ceiling(retryAfter.TotalSeconds));

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset until = now + Window;
        report = reportedUntil.AddOrUpdate(callerDigest, until, (_, previous) => previous <= now ? until : previous) == until;

        // A caller that went quiet is not remembered past its window.
        foreach (KeyValuePair<string, DateTimeOffset> entry in reportedUntil)
        {
            if (entry.Value <= now)
            {
                reportedUntil.TryRemove(entry);
            }
        }

        return false;
    }

    public void Dispose() => limiter?.Dispose();
}
