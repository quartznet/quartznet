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

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Formatting the dashboard's pages share.
/// </summary>
/// <remarks>
/// This used to read values out of whatever the API client returned by walking JSON properties and
/// reflecting over properties, because the client's trigger, calendar and job-data members were
/// untyped. They are <see cref="ITrigger" />, <see cref="ICalendar" /> and <see cref="JobDataMap" />
/// now, so the pages read them as properties and none of that is needed.
/// </remarks>
internal static class DisplayValueHelper
{
    public static string FormatKey(string? group, string? name)
    {
        string safeGroup = string.IsNullOrWhiteSpace(group) ? "DEFAULT" : group;
        string safeName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        return safeGroup + "." + safeName;
    }

    /// <summary>
    /// The outcomes a continuation waits for, in words: <c>Success</c>, <c>Failure or cancellation</c>,
    /// <c>Any outcome</c>.
    /// </summary>
    /// <remarks>
    /// A flags enum rendered by <see cref="Enum.ToString()" /> reads as <c>OnFailure, OnCancellation</c>,
    /// which is a value rather than a sentence. A condition carrying a flag this build does not know —
    /// a row written by a newer node — is shown as it came rather than as an empty cell.
    /// </remarks>
    public static string FormatContinuationCondition(ContinuationCondition condition)
    {
        if (condition == ContinuationCondition.OnAnyOutcome)
        {
            return "Any outcome";
        }

        List<string> outcomes = [];
        if (condition.HasFlag(ContinuationCondition.OnSuccess))
        {
            outcomes.Add("success");
        }

        if (condition.HasFlag(ContinuationCondition.OnFailure))
        {
            outcomes.Add("failure");
        }

        if (condition.HasFlag(ContinuationCondition.OnCancellation))
        {
            outcomes.Add("cancellation");
        }

        if (condition.HasFlag(ContinuationCondition.OnVeto))
        {
            outcomes.Add("veto");
        }

        if (outcomes.Count == 0)
        {
            return condition.ToString();
        }

        string spelled = string.Join(" or ", outcomes);
        return char.ToUpperInvariant(spelled[0]) + spelled[1..];
    }
}
