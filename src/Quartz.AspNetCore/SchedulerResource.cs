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

namespace Quartz;

/// <summary>
/// The scheduler a per-scheduler authorization policy is evaluated against: the resource passed to
/// <c>IAuthorizationService.AuthorizeAsync(user, resource, policyName)</c> by every HTTP API route and
/// every dashboard page that names a scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Write an <c>AuthorizationHandler&lt;TRequirement, SchedulerResource&gt;</c> and the same decision
/// covers both surfaces. <see cref="SchedulerName" /> is the name the caller asked for, spelled as they
/// spelled it — scheduler lookups compare names ignoring case, so a handler that matches a claim against
/// it should do the same.
/// </para>
/// <para>
/// A scheduler reached through a target — <c>w1/QuartzScheduler</c> — arrives as the bare
/// <see cref="SchedulerName" /> with the target in <see cref="Target" />, so a handler written against
/// the name keeps matching it, and one that cares which process the name was found in reads the target.
/// </para>
/// <para>
/// From 4.8 the same resource carries <see cref="Operation" /> and <see cref="Access" /> to an
/// <c>OperationAuthorizationPolicy</c>, which decides what the caller may do rather than which schedulers
/// they may see.
/// </para>
/// </remarks>
/// <param name="SchedulerName">The scheduler the request is about.</param>
public sealed record SchedulerResource(string SchedulerName)
{
    /// <summary>
    /// The target the scheduler is reached through — the first half of its key, as
    /// <see cref="SchedulerRef" /> reads it — or <see langword="null" /> for one of this process, or
    /// one registered without a target. Added in 4.5.
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// The operation asked for, by the name the HTTP API publishes as its operation id — <c>PauseJob</c>,
    /// <c>Shutdown</c>, <c>GetJobDetails</c> — or <see langword="null" /> when the question is only whether
    /// the caller may see the scheduler. Added in 4.8.
    /// </summary>
    /// <remarks>
    /// Set on the resource an <c>OperationAuthorizationPolicy</c> is evaluated against, never on the one
    /// the <c>SchedulerAuthorizationPolicy</c> is, so a handler written for the second before 4.8 is asked
    /// the same question it always was.
    /// </remarks>
    public string? Operation { get; init; }

    /// <summary>
    /// The operator role <see cref="Operation" /> needs — <see cref="SchedulerAccess.Administer" /> for a
    /// trigger-now that carries job data — or <see langword="null" /> beside a null
    /// <see cref="Operation" />. Added in 4.8.
    /// </summary>
    /// <remarks>
    /// The level the HTTP API's route table gives the operation, decided by the surface that asks, so a
    /// handler compares roles with it rather than keeping a table of its own.
    /// <c>RequireSchedulerRoles</c> is the handler Quartz ships.
    /// </remarks>
    public SchedulerAccess? Access { get; init; }

    /// <summary>
    /// The resource for a key: the scheduler's name and the target it is reached through, when it is.
    /// </summary>
    internal static SchedulerResource For(string schedulerKey)
    {
        SchedulerRef key = SchedulerRef.Parse(schedulerKey);
        return new SchedulerResource(key.SchedulerName) { Target = key.Target };
    }
}
