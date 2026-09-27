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
/// A trigger stored <c>AWAITING</c> a parent that no longer exists and is not running anywhere: the
/// row the misfire pass settles the way a deleted parent would have.
/// </summary>
/// <param name="Key">The waiting trigger.</param>
/// <param name="Parent">The trigger it waited for, whose row is gone.</param>
/// <param name="Condition">The outcome it waited on, read as <see cref="AwaitingContinuation" /> reads it.</param>
/// <seealso cref="StdAdoDelegate.SelectStrandedContinuations" />
internal sealed record StrandedContinuation(TriggerKey Key, TriggerKey Parent, ContinuationCondition Condition);
