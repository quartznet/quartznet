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
/// One trigger an acquisition claims: its key, and the next fire time it was read with, which the claim
/// holds it to.
/// </summary>
/// <remarks>
/// A claim moves the trigger's row only while it is still in the state the acquisition read and still due
/// at this time, which is what tells a row another node claimed or rescheduled in between from one that is
/// this node's to take.
/// </remarks>
public readonly record struct TriggerClaim
{
    /// <summary>
    /// The trigger to claim.
    /// </summary>
    public required TriggerKey TriggerKey { get; init; }

    /// <summary>
    /// The next fire time the acquisition read the trigger with.
    /// </summary>
    public required DateTimeOffset NextFireTimeUtc { get; init; }
}
