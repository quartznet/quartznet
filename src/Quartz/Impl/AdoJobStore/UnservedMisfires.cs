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
/// The misfired triggers in the execution groups this node does not serve (#4069).
/// </summary>
/// <param name="Count">How many there are: <c>WAITING</c> rows past the misfire cutoff that this node's sweep leaves alone.</param>
/// <param name="OldestDueUtc">The earliest fire time among them; <see langword="null" /> when there are none.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct UnservedMisfires(int Count, DateTimeOffset? OldestDueUtc)
{
    /// <summary>Nothing is misfired outside what this node serves, or the node routes nothing.</summary>
    public static UnservedMisfires None => default;
}
