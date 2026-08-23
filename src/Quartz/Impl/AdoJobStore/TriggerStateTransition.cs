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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// One conditional trigger-state change: rows currently in <see cref="From" /> become
/// <see cref="To" />, and rows in any other state are left alone.
/// </summary>
/// <remarks>
/// The blocking and unblocking of a job's triggers is always several of these at once, and every one
/// of them is a round trip on its own unless they travel together.
/// </remarks>
/// <param name="From">The state a row must currently hold to be changed.</param>
/// <param name="To">The state to write.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TriggerStateTransition(StoredTriggerState From, StoredTriggerState To);
