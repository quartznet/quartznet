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

using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore;

/// <summary>
/// The requirement <see cref="SchedulerRolesAuthorizationExtensions.RequireSchedulerRoles" /> adds, and its
/// own handler: ASP.NET Core's pass-through handler evaluates a requirement that is one, so nothing has to
/// be registered for it.
/// </summary>
/// <remarks>
/// A snapshot of <see cref="SchedulerRoles" /> taken when the policy is built, so a list changed afterwards
/// changes nothing a running policy decides.
/// </remarks>
internal sealed class SchedulerRolesRequirement : AuthorizationHandler<SchedulerRolesRequirement, SchedulerResource>, IAuthorizationRequirement
{
    private readonly string[] viewers;
    private readonly string[] operators;
    private readonly string[] administrators;
    private readonly FrozenDictionary<string, SchedulerAccess> operations;

    private SchedulerRolesRequirement(string[] viewers, string[] operators, string[] administrators, FrozenDictionary<string, SchedulerAccess> operations)
    {
        this.viewers = viewers;
        this.operators = operators;
        this.administrators = administrators;
        this.operations = operations;
    }

    /// <summary>
    /// The requirement <paramref name="roles" /> describe, once they have been checked.
    /// </summary>
    /// <remarks>
    /// The refusals name <c>roles</c>, what the caller's callback wrote, and the message says which list or
    /// which entry.
    /// </remarks>
    /// <exception cref="ArgumentException">See <see cref="SchedulerRolesAuthorizationExtensions.RequireSchedulerRoles" />.</exception>
    public static SchedulerRolesRequirement From(SchedulerRoles roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        string[] viewers = Checked(roles.Viewers, nameof(SchedulerRoles.Viewers));
        string[] operators = Checked(roles.Operators, nameof(SchedulerRoles.Operators));
        string[] administrators = Checked(roles.Administrators, nameof(SchedulerRoles.Administrators));

        if (viewers.Length + operators.Length + administrators.Length == 0)
        {
            throw new ArgumentException(
                "Name at least one role in Viewers, Operators or Administrators: a policy that grants nobody anything refuses every request, reads included.",
                nameof(roles));
        }

        Dictionary<string, SchedulerAccess> operations = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, SchedulerAccess access) in roles.Operations)
        {
            WireRoute route = SchedulerRoutes.Find(name)
                ?? throw new ArgumentException($"Operations names '{name}', which is no operation of the HTTP API. The names are its operation ids: PauseJob, AddCalendar, Shutdown.", nameof(roles));

            if (!route.Mutates)
            {
                throw new ArgumentException($"Operations names '{route.Name}', which is a read: every role may call it, so there is nothing to move.", nameof(roles));
            }

            if (access is not (SchedulerAccess.Operate or SchedulerAccess.Administer))
            {
                throw new ArgumentException($"Operations['{route.Name}'] is {access}; an operation that changes something needs Operate or Administer.", nameof(roles));
            }

            operations[route.Name] = access;
        }

        return new SchedulerRolesRequirement(viewers, operators, administrators, operations.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    private static string[] Checked(List<string> roles, string list)
    {
        foreach (string role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException($"{list} holds a blank role, which no principal is in.", nameof(roles));
            }
        }

        return [.. roles];
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SchedulerRolesRequirement requirement, SchedulerResource resource)
    {
        if (Granted(context.User) is { } granted && granted >= Required(resource))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The level <paramref name="resource" /> asks for, with this policy's overrides applied.
    /// </summary>
    /// <remarks>
    /// An override restates the level of the route it names; a trigger-now that carries job data asks for
    /// more than its route's level, and keeps asking for it whatever the override says.
    /// </remarks>
    internal SchedulerAccess Required(SchedulerResource resource)
    {
        WireRoute? route = resource.Operation is { } operation ? SchedulerRoutes.Find(operation) : null;
        SchedulerAccess asked = resource.Access ?? route?.Access ?? SchedulerAccess.View;

        if (route is null || !operations.TryGetValue(route.Name, out SchedulerAccess overridden))
        {
            return asked;
        }

        return asked > route.Access && asked > overridden ? asked : overridden;
    }

    /// <summary>
    /// The highest level a role of <paramref name="user" /> grants, or <see langword="null" /> for none.
    /// </summary>
    private SchedulerAccess? Granted(ClaimsPrincipal user)
    {
        if (InAny(user, administrators))
        {
            return SchedulerAccess.Administer;
        }

        if (InAny(user, operators))
        {
            return SchedulerAccess.Operate;
        }

        return InAny(user, viewers) ? SchedulerAccess.View : null;
    }

    private static bool InAny(ClaimsPrincipal user, string[] roles)
    {
        foreach (string role in roles)
        {
            if (user.IsInRole(role))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString()
    {
        return $"{nameof(SchedulerRolesRequirement)}: Viewers [{string.Join(", ", viewers)}], Operators [{string.Join(", ", operators)}], "
               + $"Administrators [{string.Join(", ", administrators)}]";
    }
}
