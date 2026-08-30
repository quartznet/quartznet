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
/// A trigger listing entry: everything a listing needs — including the state and execution
/// group that previously required one extra round trip per trigger — without materializing
/// the trigger or its <see cref="JobDataMap" />.
/// </summary>
/// <param name="Key">The trigger's key.</param>
/// <param name="JobKey">The key of the job the trigger fires.</param>
/// <param name="Description">The trigger's description, if one was given.</param>
/// <param name="TriggerType">The store's trigger type discriminator, for example <c>"CRON"</c> or
/// <c>"SIMPLE"</c> — the value of the <c>TRIGGER_TYPE</c> column, which is what
/// <see cref="Quartz.Impl.AdoJobStore.ITriggerPersistenceDelegate.GetHandledTriggerTypeDiscriminator" />
/// returns. It is a discriminator, <em>not</em> a type name, and so is deliberately not called one:
/// contrast <see cref="JobHeader.JobTypeName" /> and
/// <see cref="Quartz.Impl.AdoJobStore.TriggerAcquireResult.JobTypeName" />, both of which carry a CLR
/// type name that a type loader can resolve.</param>
/// <param name="State">The trigger's current state.</param>
/// <param name="StartTimeUtc">The time the trigger's schedule comes into effect.</param>
/// <param name="EndTimeUtc">The time the trigger's schedule ends, if bounded.</param>
/// <param name="NextFireTimeUtc">The next time the trigger will fire, if any.</param>
/// <param name="PreviousFireTimeUtc">The previous time the trigger fired, if any.</param>
/// <param name="CalendarName">The name of the calendar the trigger observes, if any.</param>
/// <param name="Priority">The trigger's priority.</param>
/// <param name="ExecutionGroup">The trigger's execution group, if any.</param>
/// <param name="RetryPolicy">The trigger's retry policy in its stored form, if it has one. A string
/// rather than a <see cref="Quartz.RetryPolicy" /> because a listing reports the column: a row whose
/// policy a newer node wrote in a shape this one cannot read still has to list.</param>
/// <param name="RetryAttempt">How many times the occurrence currently being executed has already been
/// retried; <c>0</c> when it has not.</param>
public sealed record TriggerHeader(
    TriggerKey Key,
    JobKey JobKey,
    string? Description,
    string TriggerType,
    TriggerState State,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc,
    string? CalendarName,
    int Priority,
    string? ExecutionGroup,
    string? RetryPolicy,
    int RetryAttempt);
