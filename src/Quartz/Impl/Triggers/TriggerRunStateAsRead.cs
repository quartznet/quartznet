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

using System.Runtime.InteropServices;

namespace Quartz.Impl.Triggers;

/// <summary>
/// The fire times of a stored trigger as somebody read it, outside the store's lock, to work a replacement
/// out from: what the replace is held to (#4324).
/// </summary>
/// <remarks>
/// Every firing moves both, and whatever else changes a trigger's run state moves at least one of them, so
/// a row whose two still match is a row nothing has fired since the read.
/// </remarks>
/// <param name="NextFireTimeUtc">The next fire time the read found.</param>
/// <param name="PreviousFireTimeUtc">The previous fire time the read found.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TriggerRunStateAsRead(DateTimeOffset? NextFireTimeUtc, DateTimeOffset? PreviousFireTimeUtc)
{
    /// <summary>
    /// The run state <paramref name="read" /> was read with.
    /// </summary>
    public static TriggerRunStateAsRead Of(ITrigger read)
    {
        return new TriggerRunStateAsRead(read.NextFireTimeUtc, read.PreviousFireTimeUtc);
    }

    /// <summary>
    /// Whether <paramref name="stored" />, the trigger as the store holds it now, has the run state it was
    /// read with.
    /// </summary>
    public bool StillDescribes(ITrigger stored)
    {
        return NextFireTimeUtc == stored.NextFireTimeUtc && PreviousFireTimeUtc == stored.PreviousFireTimeUtc;
    }
}
