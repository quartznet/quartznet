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

using System;

namespace Quartz.Core;

/// <summary>
/// Thrown by a job store that declines to start at all, before it has created anything. It is a
/// <see cref="SchedulerException" /> to the caller; the distinct type only tells
/// <see cref="QuartzScheduler.Start" /> that nothing was set up, so the start marker can be released
/// and a corrected retry can run the full start-up sequence rather than the resume path.
/// </summary>
[Serializable]
internal sealed class SchedulerStartRefusedException : SchedulerException
{
    internal SchedulerStartRefusedException(string message) : base(message)
    {
    }
}
