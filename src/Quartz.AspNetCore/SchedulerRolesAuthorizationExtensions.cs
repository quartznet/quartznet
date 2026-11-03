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

using Microsoft.AspNetCore.Authorization;

using Quartz.AspNetCore;

namespace Quartz;

/// <summary>
/// Builds an operation policy out of the application's roles. Added in 4.8.
/// </summary>
public static class SchedulerRolesAuthorizationExtensions
{
    /// <summary>
    /// Requires the role an operation on a scheduler needs: a role in
    /// <see cref="SchedulerRoles.Viewers" />, <see cref="SchedulerRoles.Operators" /> or
    /// <see cref="SchedulerRoles.Administrators" /> at or above the <see cref="SchedulerResource.Access" />
    /// the HTTP API, the dashboard or the agent asks with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Name the policy this builds in <c>QuartzHttpApiOptions.OperationAuthorizationPolicy</c> and
    /// <c>QuartzDashboardOptions.OperationAuthorizationPolicy</c>; it is evaluated against a
    /// <see cref="SchedulerResource" />, and against nothing else it succeeds for nobody. The requirement is
    /// its own handler, so nothing has to be registered beside <c>AddAuthorization()</c>.
    /// </para>
    /// <para>
    /// It says nothing about which schedulers a caller may see, which is what a
    /// <c>SchedulerAuthorizationPolicy</c> is for; the two compose. Nor does it authenticate anybody: put
    /// <c>RequireAuthenticatedUser()</c> in the same policy, or <c>RequireAuthorization()</c> on the mapped
    /// endpoints, to have an anonymous caller challenged rather than refused.
    /// </para>
    /// </remarks>
    /// <param name="builder">The policy being built.</param>
    /// <param name="configure">Names the roles, and any per-operation overrides.</param>
    /// <returns>The same builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> or <paramref name="configure" /> is null.</exception>
    /// <exception cref="ArgumentException">
    /// No role is named, a role is blank, or <see cref="SchedulerRoles.Operations" /> names an operation the
    /// HTTP API does not publish, a read, or a change at <see cref="SchedulerAccess.View" />.
    /// </exception>
    public static AuthorizationPolicyBuilder RequireSchedulerRoles(this AuthorizationPolicyBuilder builder, Action<SchedulerRoles> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        SchedulerRoles roles = new();
        configure(roles);

        return builder.AddRequirements(SchedulerRolesRequirement.From(roles));
    }
}
