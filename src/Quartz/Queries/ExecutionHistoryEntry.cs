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
/// One execution that has finished, as the scheduler that ran it reported.
/// </summary>
/// <remarks>
/// <see cref="Duration" /> is a <see cref="TimeSpan" />, as every other duration Quartz reports is.
/// </remarks>
/// <param name="SchedulerName">The scheduler the execution belongs to.</param>
/// <param name="SchedulerInstanceId">
/// The node that ran it. Every node of a cluster keeps its own history of its own executions, so
/// without this a row cannot say which machine it came from — and a store shared across the cluster
/// cannot say it either.
/// </param>
/// <param name="JobGroup">The group of the job that ran.</param>
/// <param name="JobName">The name of the job that ran.</param>
/// <param name="TriggerGroup">The group of the trigger that fired it.</param>
/// <param name="TriggerName">The name of the trigger that fired it.</param>
/// <param name="FiredAtUtc">When the execution fired.</param>
/// <param name="Duration">How long the job took.</param>
/// <param name="Succeeded">Whether the job completed without throwing.</param>
/// <param name="ExceptionMessage">What it threw, or <see langword="null" /> when it succeeded.</param>
public sealed record ExecutionHistoryEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage);
