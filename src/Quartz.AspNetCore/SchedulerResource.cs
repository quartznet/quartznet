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
/// Write an <c>AuthorizationHandler&lt;TRequirement, SchedulerResource&gt;</c> and the same decision
/// covers both surfaces. <see cref="SchedulerName" /> is the name the caller asked for, spelled as they
/// spelled it — scheduler lookups compare names ignoring case, so a handler that matches a claim against
/// it should do the same.
/// </remarks>
/// <param name="SchedulerName">The scheduler the request is about.</param>
public sealed record SchedulerResource(string SchedulerName);
