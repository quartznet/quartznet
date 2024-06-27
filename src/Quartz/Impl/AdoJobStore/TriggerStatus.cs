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
/// Object representing a job or trigger key.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
/// <remarks>
/// Construct a new TriggerStatus with the status name and nextFireTime.
/// </remarks>
public class TriggerStatus(
    string status,
    DateTimeOffset? nextFireTime,
    TriggerKey triggerKey,
    JobKey jobKey)
{
    public JobKey JobKey { get; set; } = jobKey;

    public TriggerKey Key { get; set; } = triggerKey;

    public string Status { get; } = status;

    public DateTimeOffset? NextFireTimeUtc { get; } = nextFireTime;

    /// <summary>
    /// Return the string representation of the TriggerStatus.
    /// </summary>
    /// <returns></returns>
    public override string ToString() => "status: " + Status + ", next Fire = " + NextFireTimeUtc;
}