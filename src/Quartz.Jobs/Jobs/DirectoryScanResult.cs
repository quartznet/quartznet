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

namespace Quartz.Jobs;

/// <summary>
/// What one pass of <see cref="DirectoryScanJob" /> over a directory found.
/// </summary>
/// <remarks>
/// Internal because the scan is: <see cref="DirectoryScanJob.Execute" /> is not virtual, so the
/// per-directory pass was never something a subclass could take part in.
/// <see cref="IDirectoryScanListener" /> is the seam, and it is handed the files themselves.
/// </remarks>
/// <param name="All">
/// Every file the scan matched. This is what the next scan compares against to notice a deletion.
/// </param>
/// <param name="Updated">
/// The files written since the previous scan and old enough to be considered settled.
/// </param>
/// <param name="Deleted">
/// The files the previous scan saw and this one did not.
/// </param>
internal readonly record struct DirectoryScanResult(
    IReadOnlyList<FileInfo> All,
    IReadOnlyList<FileInfo> Updated,
    IReadOnlyList<FileInfo> Deleted);
