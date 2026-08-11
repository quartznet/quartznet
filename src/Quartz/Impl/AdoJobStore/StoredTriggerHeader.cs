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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The parts of a stored trigger row a state transition decides on: the trigger's stored state, the job
/// it fires and when it fires next.
/// </summary>
/// <remarks>
/// The storage-side counterpart of <see cref="TriggerHeader" />, which is what a listing reports. This one
/// speaks <see cref="StoredTriggerState" /> rather than <see cref="TriggerState" />, because resuming a
/// trigger has to tell <see cref="StoredTriggerState.PausedBlocked" /> from
/// <see cref="StoredTriggerState.Paused" /> and the reported state does not.
/// </remarks>
/// <param name="Key">The trigger's key.</param>
/// <param name="JobKey">The key of the job the trigger fires.</param>
/// <param name="State">The trigger's stored state.</param>
/// <param name="NextFireTimeUtc">The next time the trigger will fire, if any.</param>
public sealed record StoredTriggerHeader(
    TriggerKey Key,
    JobKey JobKey,
    StoredTriggerState State,
    DateTimeOffset? NextFireTimeUtc);
