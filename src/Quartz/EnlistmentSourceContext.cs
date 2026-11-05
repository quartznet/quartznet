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

namespace Quartz;

/// <summary>
/// What <see cref="AdoJobStoreOptions.EnlistmentSource" /> is told about the scheduler call that is
/// beginning.
/// </summary>
/// <remarks>
/// A context object rather than a parameter, so that the next thing a source needs to know about a call is
/// a property rather than a new delegate type. It exists only while a source is set: a scheduler with none
/// builds nothing.
/// </remarks>
public sealed class EnlistmentSourceContext
{
    /// <summary>
    /// The scheduler the call was made on, which is what
    /// <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" /> and
    /// <see cref="SchedulerEnlistmentExtensions.EnlistConnection" /> enlist on.
    /// </summary>
    /// <remarks>
    /// Only for enlisting. A call on it that reads or writes the store would be asked about in turn, and the
    /// source would be called again from inside itself.
    /// </remarks>
    public required IScheduler Scheduler { get; init; }
}
