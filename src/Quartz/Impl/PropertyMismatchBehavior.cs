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
/// What <see cref="PropertySettingJobFactory" /> does when a <see cref="JobDataMap" /> entry does not
/// correspond to a settable property on the job class, or cannot be converted to its type.
/// </summary>
public enum PropertyMismatchBehavior
{
    /// <summary>
    /// Leave the entry where it is and say nothing. This is the default: a data map is allowed to carry
    /// values a job reads for itself rather than receives through a property.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// Log a warning naming the job type and the entry, and carry on. Useful for finding a misspelled
    /// property name, noisy when a map deliberately carries extra entries.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Fail the instantiation with a <see cref="SchedulerException" />, which moves the job's triggers
    /// into <see cref="TriggerState.Error" />.
    /// </summary>
    Throw = 2
}
