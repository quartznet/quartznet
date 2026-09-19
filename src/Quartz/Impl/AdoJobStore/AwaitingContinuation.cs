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
/// One trigger found waiting for a parent's firing, and the condition it waits on.
/// </summary>
/// <remarks>
/// Everything the completion needs to decide release from discard, and nothing else: the trigger
/// itself is not materialized, because settling one is a statement against its row rather than
/// anything that has to compute a schedule.
/// </remarks>
/// <param name="Key">The awaiting trigger.</param>
/// <param name="Condition">The outcomes of the parent's firing that release it.</param>
/// <seealso cref="IDriverDelegate.SelectAwaitingContinuations" />
public sealed record AwaitingContinuation(TriggerKey Key, ContinuationCondition Condition);
