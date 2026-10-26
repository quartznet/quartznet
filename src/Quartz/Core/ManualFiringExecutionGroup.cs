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

using System;
using System.Collections.Generic;

using Quartz.Spi;

namespace Quartz.Core;

/// <summary>
/// The execution group a manual firing counts against: the one group every stored trigger of its job
/// carries (#4092).
/// </summary>
/// <remarks>
/// <para>
/// There is none when the job has no triggers, when one of them carries no group, or when two carry
/// different groups. Groups compare ordinally, as execution limits count them.
/// </para>
/// <para>
/// A store answers through <see cref="INextVersionJobStore.GetSharedExecutionGroup" /> without loading the
/// triggers (#4167); <see cref="SharedBy" /> is the rule over triggers already loaded, for a store of
/// another make.
/// </para>
/// </remarks>
internal static class ManualFiringExecutionGroup
{
    /// <summary>
    /// The one group every one of <paramref name="triggers" /> carries.
    /// </summary>
    /// <returns>The group, or <see langword="null" /> for none.</returns>
    internal static string? SharedBy(IEnumerable<IOperableTrigger> triggers)
    {
        string? shared = null;
        foreach (IOperableTrigger trigger in triggers)
        {
            if (!Shares(ref shared, (trigger as INextVersionTrigger)?.ExecutionGroup))
            {
                return null;
            }
        }

        return shared;
    }

    /// <summary>
    /// Takes one more trigger's group into the group the triggers before it share.
    /// </summary>
    /// <param name="shared">
    /// The group the triggers before it share, <see langword="null" /> before the first; the group with
    /// this trigger's taken in when the answer is <see langword="true" />.
    /// </param>
    /// <param name="group">The trigger's group, <see langword="null" /> for none.</param>
    /// <returns>
    /// <see langword="false" /> once the triggers share no group: this one carries none, or another than
    /// the ones before it.
    /// </returns>
    internal static bool Shares(ref string? shared, string? group)
    {
        if (group == null || (shared != null && !string.Equals(shared, group, StringComparison.Ordinal)))
        {
            return false;
        }

        shared = group;
        return true;
    }
}
