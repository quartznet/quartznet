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
/// A pause as a store recorded it: why, who asked, and when.
/// </summary>
/// <remarks>
/// What <see cref="IScheduler.GetTriggerPause" />, <see cref="IScheduler.GetTriggerGroupPause" /> and
/// <see cref="IScheduler.GetJobGroupPause" /> answer, and what <see cref="TriggerHeader.Pause" /> carries.
/// A pause made through a reasonless member is recorded too, with both texts <see langword="null" />.
/// </remarks>
/// <param name="Reason">Why, as <see cref="PauseDetails.Reason" /> said it, cut to
/// <see cref="PauseDetails.MaxReasonLength" />; <see langword="null" /> when nothing was said.</param>
/// <param name="RequestedBy">Who asked, cut to <see cref="PauseDetails.MaxRequestedByLength" />;
/// <see langword="null" /> when nobody said.</param>
/// <param name="PausedAtUtc">When the pause was made, on the scheduler's clock.</param>
public sealed record PauseInfo(string? Reason, string? RequestedBy, DateTimeOffset PausedAtUtc);
