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
/// The members of <c>Quartz.TriggerConstants</c> this assembly reads.
/// </summary>
/// <remarks>
/// Carried rather than linked, because the shipped type is public and linking a public type into an
/// analyzer would publish it from here too: a constant, a process-lifetime
/// <see langword="readonly" />, and the priority below, none of which two copies can drift on
/// observably. The year bound is what stops a search for the next fire time of an expression that
/// has none — <c>0 0 0 30 2 ?</c>, the thirtieth of February — from running forever.
/// </remarks>
internal static class TriggerConstants
{
    /// <summary>
    /// What a trigger's priority is when nothing sets one, which is what
    /// <c>[CronTrigger].Priority</c> defaults to and therefore the value the generator emits no
    /// <c>WithPriority</c> call for.
    /// </summary>
    internal const int DefaultPriority = 5;

    /// <summary>
    /// A century out from the moment the process started. <c>TimeProvider</c> is a net8.0 type and
    /// this compilation is netstandard2.0; the value is the same reading of the same clock.
    /// </summary>
    internal static readonly int YearToGiveUpSchedulingAt = DateTimeOffset.UtcNow.Year + 100;

    internal const int EarliestYear = 1970;
}
