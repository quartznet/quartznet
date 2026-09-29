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

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz;

/// <summary>
/// One job the Quartz health check requires to have succeeded recently: an entry of
/// <see cref="QuartzHealthCheckOptions.RequiredJobs" />.
/// </summary>
/// <remarks>
/// <para>
/// The job is named by <see cref="Name" /> and <see cref="Group" /> rather than by a <see cref="JobKey" />,
/// so a configuration section binds onto it: <c>{ "Name": "nightly-report", "Group": "reports",
/// "SucceededWithin": "1.02:00:00" }</c>. In code, <see cref="QuartzHealthCheckOptions.RequireSuccessWithin" />
/// adds one.
/// </para>
/// <para>
/// The requirement is met while the job's <see cref="JobRunStatus.LastSucceededAtUtc" /> is within
/// <see cref="SucceededWithin" /> of now, on the scheduler's clock. A skipped run counts as a success.
/// A job with no recorded success is judged from the first time the check evaluated it.
/// </para>
/// </remarks>
public sealed class RequiredJobOptions
{
    /// <summary>
    /// The job's name. Required.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The job's group: <see cref="Key{T}.DefaultGroup" /> unless set.
    /// </summary>
    public string Group { get; set; } = JobKey.DefaultGroup;

    /// <summary>
    /// How long ago the job's last success may have fired. Must be positive.
    /// </summary>
    /// <remarks>
    /// Leave room for the job's own schedule, its run time and a retry: a nightly job wants a little over a
    /// day, <c>1.02:00:00</c> say.
    /// </remarks>
    public TimeSpan SucceededWithin { get; set; }

    /// <summary>
    /// What the check reports while the requirement is not met: <see cref="HealthStatus.Degraded" /> unless
    /// set. <see cref="HealthStatus.Unhealthy" /> is for a job that must never be late; <see cref="HealthStatus.Healthy" />
    /// is refused.
    /// </summary>
    public HealthStatus Status { get; set; } = HealthStatus.Degraded;
}
