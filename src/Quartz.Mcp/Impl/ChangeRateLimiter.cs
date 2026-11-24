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

using System.Threading.RateLimiting;

using Microsoft.Extensions.Options;

namespace Quartz.Mcp.Impl;

/// <summary>
/// <see cref="QuartzMcpOptions.MaxChangesPerMinute" />: a sliding window of a minute per caller, over every tool that
/// changes something.
/// </summary>
/// <remarks>
/// Partitioned by the caller's digest, so one agent's loop does not starve another, and the identity is not kept.
/// Four segments, no queue: a change over the limit is refused at once with when to try again, never held. A
/// preview and a read are not counted. The count is this process's.
/// </remarks>
internal sealed class ChangeRateLimiter : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<string>? limiter;

    public ChangeRateLimiter(IOptions<QuartzMcpOptions> options)
    {
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
    /// Counts one change for <paramref name="callerDigest" />: <see langword="false" /> when it is over the limit,
    /// with when to try again.
    /// </summary>
    public bool TryAcquire(string callerDigest, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
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
        return false;
    }

    public void Dispose() => limiter?.Dispose();
}
