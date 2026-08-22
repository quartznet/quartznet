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

public sealed class SchedulingOptions
{
    /// <summary>
    /// Whether the existing scheduling data (with same identifiers) will be
    /// overwritten.
    /// </summary>
    /// <remarks>
    /// If false, and <see cref="IgnoreDuplicates" /> is not false, and jobs or
    /// triggers with the same names already exist as those in the file, an
    /// error will occur.
    /// </remarks>
    /// <seealso cref="IgnoreDuplicates" />
    public bool OverwriteExistingData { get; set; } = true;

    /// <summary>
    /// If true (and <see cref="OverwriteExistingData" /> is false) then any
    /// job/triggers encountered in this file that have names that already exist
    /// in the scheduler will be ignored, and no error will be produced.
    /// </summary>
    /// <seealso cref="OverwriteExistingData"/>
    public bool IgnoreDuplicates { get; set; }

    /// <summary>
    /// If true (and <see cref="OverwriteExistingData" /> is true) then any
    /// job/triggers encountered in this file that already exist is scheduler
    /// will be updated with start time relative to old trigger. Effectively
    /// new trigger's last fire time will be updated to old trigger's last fire time
    /// and trigger's next fire time will updated to be next from this last fire time.
    /// </summary>
    public bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }
}