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
/// The triggers pinned to this node that a firing on another node holds <c>BLOCKED</c> (#3988).
/// </summary>
/// <param name="Count">How many there are, as rows of the statement that counts them.</param>
/// <param name="LatestBlockingFiredUtc">
/// When the latest of the firings holding them was fired; <see langword="null" /> when none is, or when no
/// firing row says. A later one than the last look found is the job having changed hands in between.
/// </param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct PinnedTriggersBlocked(int Count, DateTimeOffset? LatestBlockingFiredUtc)
{
    /// <summary>Nothing pinned to this node is held, or the delegate cannot say.</summary>
    public static PinnedTriggersBlocked None => default;
}
