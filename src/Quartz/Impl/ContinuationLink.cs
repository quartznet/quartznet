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

namespace Quartz.Impl;

/// <summary>
/// One trigger on a continuation's chain of parents, as the store will hold it once the call storing the
/// continuation is done: whether there is one under the key, and the trigger it waits for.
/// </summary>
/// <param name="Held">Whether a trigger is stored, or being stored, under the key.</param>
/// <param name="WaitsFor">
/// The trigger it waits for, or <see langword="null" /> when it waits for nothing: an ordinary trigger, a
/// released continuation, or one parked in error, which runs rather than waits once it is reset.
/// </param>
/// <seealso cref="ContinuationChainWalk" />
internal readonly record struct ContinuationLink(bool Held, TriggerKey? WaitsFor)
{
    /// <summary>
    /// No trigger under the key.
    /// </summary>
    public static ContinuationLink NotHeld => default;
}
