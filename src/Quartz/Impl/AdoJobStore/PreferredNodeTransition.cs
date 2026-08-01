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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The compare-and-swap <see cref="IDriverDelegate.UpdateTriggerPreferredNodeConditional" /> attempts:
/// the pin the caller observed, and the pin it wants to put in its place.
/// </summary>
/// <remarks>
/// Keeping the two pins as one value is what makes the operation readable as a CAS — the alternative,
/// four loose parameters in expected/new pairs, is trivial to transpose and the compiler cannot tell.
/// </remarks>
public sealed record PreferredNodeTransition
{
    /// <summary>
    /// The pin the row must still hold for the swap to happen. Read at acquisition time; if anything
    /// re-pinned the trigger since, no row matches and the swap reports zero rows updated.
    /// </summary>
    public required PreferredNode Expected { get; init; }

    /// <summary>
    /// The pin to write when the row still holds <see cref="Expected" />.
    /// </summary>
    public required PreferredNode New { get; init; }
}
