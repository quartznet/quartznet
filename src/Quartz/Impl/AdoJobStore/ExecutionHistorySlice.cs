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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Which rows of the execution feed a sweep statement trims, beside how old they are.
/// </summary>
/// <remarks>
/// The whole feed when both are <see langword="null" />, which is the longest window's slice.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExecutionHistorySlice
{
    /// <summary>
    /// Only the rows whose effective result is one of these: a retention tier's slice. A set naming no
    /// result this version knows matches nothing.
    /// </summary>
    public IReadOnlyCollection<JobRunResult>? Results { get; init; }

    /// <summary>
    /// Only this job's rows that did not fail: the per-job cap's slice, which exempts failures.
    /// </summary>
    public JobKey? CappedJob { get; init; }
}
