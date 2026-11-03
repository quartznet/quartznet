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

namespace Quartz.Dashboard.Services;

/// <summary>
/// A call <see cref="QuartzDashboardOptions.OperationAuthorizationPolicy" /> refused for the visitor's role.
/// </summary>
/// <remarks>
/// An <see cref="UnauthorizedAccessException" />, as the dashboard's other refusals are, so a caller that
/// catches those catches this. Its own type so the Action Log can record it as refused rather than failed:
/// nothing went wrong, a rule said no.
/// </remarks>
internal sealed class OperationRefusedException : UnauthorizedAccessException
{
    public OperationRefusedException(string operation, string schedulerKey, SchedulerAccess required)
        : base($"Not authorized for {operation} on scheduler '{schedulerKey}': it needs {required}.")
    {
        Operation = operation;
        SchedulerKey = schedulerKey;
        Required = required;
    }

    /// <summary>
    /// What was refused: the route's name, with <c>with job data</c> after a trigger-now refused for its data.
    /// </summary>
    public string Operation { get; }

    /// <summary>The scheduler's key, as the dashboard spells it.</summary>
    public string SchedulerKey { get; }

    /// <summary>The operator role the call needed.</summary>
    public SchedulerAccess Required { get; }
}
