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
/// Thrown inside a completion that runs without <see cref="SchedulerLock.TriggerAccess" /> when its
/// first statement finds triggers awaiting the firing, before anything has been written. The
/// transaction is rolled back and the whole completion runs again under the lock, where settling a
/// continuation may read paused-group and blocked state that only the lock keeps still.
/// </summary>
/// <remarks>
/// A <see cref="JobPersistenceException" /> so that the store's guards and its transaction wrapper let
/// it through as itself: neither wraps one, and the wrapper rolls back and rethrows without retrying,
/// since nothing about it is transient.
/// </remarks>
internal sealed class CompletionNeedsLockException : JobPersistenceException
{
    public CompletionNeedsLockException()
        : base("The firing has continuations awaiting it, which are settled under TRIGGER_ACCESS.")
    {
    }
}
