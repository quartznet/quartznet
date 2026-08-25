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

namespace Quartz.Extensibility;

/// <summary>
/// What every <c>ExcludedJobTypeNames</c> property accepts, in one place.
/// </summary>
/// <remarks>
/// The rule has to hold on both the store-level <see cref="TriggerAcquisitionRequest" /> and the
/// delegate-level <c>TriggerAcquisitionCriteria</c>, because a derived ADO.NET store sets the
/// exclusions on the criteria and never touches the request. Checking only one of them would leave
/// the documented path unguarded.
/// </remarks>
internal static class JobTypeExclusions
{
    /// <summary>
    /// The most names an exclusion set may hold. Oracle refuses an <c>IN</c> list of more than a
    /// thousand expressions, and a clear error here beats ORA-01795 at two in the morning.
    /// </summary>
    internal const int MaxNames = 1000;

    /// <summary>
    /// Returns the names unchanged, having established that the acquisition query built from them
    /// will mean what the caller intended.
    /// </summary>
    /// <remarks>
    /// A blank entry is rejected rather than skipped because of what it would otherwise become: one
    /// <see langword="null" /> in the list makes the clause <c>NOT IN (…, NULL)</c>, which evaluates
    /// to UNKNOWN for every row, and the node then quietly acquires nothing at all — the worst way
    /// for a filter to fail.
    /// </remarks>
    /// <exception cref="ArgumentException">An entry is blank, or there are too many of them.</exception>
    internal static IReadOnlyCollection<string>? Validated(IReadOnlyCollection<string>? names, string parameterName)
    {
        if (names is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Throw.ArgumentException("ExcludedJobTypeNames must not contain null, empty, or whitespace entries.", parameterName);
            }
        }

        if (names.Count > MaxNames)
        {
            Throw.ArgumentException($"ExcludedJobTypeNames must not exceed {MaxNames} entries.", parameterName);
        }

        return names;
    }
}
