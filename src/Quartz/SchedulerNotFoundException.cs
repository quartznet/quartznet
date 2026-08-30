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
/// Thrown by <see cref="SchedulerFactoryExtensions.GetRequiredScheduler" /> when the container the
/// factory belongs to has no scheduler of the requested name.
/// </summary>
/// <remarks>
/// A type of its own rather than a bare <see cref="SchedulerException" />, because "the name is not
/// registered here" is the one failure a caller of that method can act on — retrying a different name,
/// or reporting the one that was misspelled — and telling it apart by message text is not a contract.
/// <see cref="ISchedulerFactory.LookupScheduler" /> is the form that answers <see langword="null" />
/// for the same question.
/// </remarks>
public sealed class SchedulerNotFoundException : SchedulerException
{
    internal SchedulerNotFoundException(string schedulerName, string message) : base(message)
    {
        SchedulerName = schedulerName;
    }

    /// <summary>
    /// The name that was asked for, so a caller can report it without parsing the message.
    /// </summary>
    public string SchedulerName { get; }
}
