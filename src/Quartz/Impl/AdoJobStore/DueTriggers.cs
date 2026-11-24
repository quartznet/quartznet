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
/// The due backlog of one execution group (#4401).
/// </summary>
/// <param name="ExecutionGroup">The group; <see langword="null" /> for triggers that name none.</param>
/// <param name="Count">How many <c>WAITING</c> triggers in it have a fire time that has passed.</param>
/// <param name="EarliestDueUtc">The earliest of those fire times.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct DueTriggers(string? ExecutionGroup, long Count, DateTimeOffset EarliestDueUtc);
