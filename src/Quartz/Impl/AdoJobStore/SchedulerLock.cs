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
/// The locks a job store takes to serialize access to the scheduling data it shares with the other
/// nodes of a cluster.
/// </summary>
/// <remarks>
/// There have only ever been these two, and an <see cref="ILockHandler" /> that was handed anything
/// else threw. Saying so in the type means a caller cannot invent a third lock that silently
/// protects nothing, and it keeps the stored lock names out of every signature that mentions a lock.
/// </remarks>
public enum SchedulerLock
{
    /// <summary>
    /// Guards every change to jobs, triggers and calendars, and the acquisition of triggers to fire.
    /// Stored as <c>TRIGGER_ACCESS</c>.
    /// </summary>
    TriggerAccess,

    /// <summary>
    /// Guards the cluster check-in and failed-node recovery, which run on their own transaction so
    /// that they cannot deadlock against trigger work. Stored as <c>STATE_ACCESS</c>.
    /// </summary>
    StateAccess
}

/// <summary>
/// Maps <see cref="SchedulerLock" /> onto the names the LOCKS table holds.
/// </summary>
/// <remarks>
/// Internal because the stored names are only ever needed where rows are written; an
/// <see cref="ILockHandler" /> that locks somewhere other than the database names its own keys.
/// </remarks>
internal static class SchedulerLockExtensions
{
    /// <summary>
    /// The value stored in the <c>LOCK_NAME</c> column for this lock. Unchanged since 1.0 - the
    /// column is the contract shared with every other node in the cluster.
    /// </summary>
    internal static string ToLockName(this SchedulerLock schedulerLock)
    {
        switch (schedulerLock)
        {
            case SchedulerLock.TriggerAccess:
                return "TRIGGER_ACCESS";
            case SchedulerLock.StateAccess:
                return "STATE_ACCESS";
            default:
                Throw.ArgumentOutOfRangeException(nameof(schedulerLock), $"Unknown scheduler lock '{schedulerLock}'");
                return null!;
        }
    }
}
