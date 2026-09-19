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
/// One scheduler a container knows about, as reported by
/// <see cref="ISchedulerRegistry.QuerySchedulers" />.
/// </summary>
/// <param name="Name">
/// The scheduler's name, spelled as it was registered. For a named registration this is the name
/// <c>AddQuartz(name, …)</c> was given, which is also the service key and the options name; for the
/// default scheduler it is its configured <see cref="QuartzSchedulerOptions.InstanceName" />.
/// </param>
/// <param name="Origin">Where the scheduler came from.</param>
/// <param name="Status">
/// What state the scheduler is in, or <see langword="null" /> when no scheduler exists under this name.
/// Null is the answer this query exists to give: the registration is there, nothing has been built from
/// it, and asking did not build it.
/// <para>
/// A scheduler that has been <em>shut down</em> also reads as null rather than as
/// <see cref="SchedulerStatus.Shutdown" />, because
/// <see cref="Extensibility.ISchedulerRepository" /> drops a shut-down scheduler as soon as a read
/// notices it. So null means "no live scheduler under this name" — for a registration, either not yet or
/// not any more. The two are not worth telling apart here: a shut-down scheduler cannot be created again
/// within the same container, so neither is a name you can get a working scheduler out of.
/// </para>
/// </param>
public sealed record SchedulerRegistration(string Name, SchedulerOrigin Origin, SchedulerStatus? Status)
{
    /// <summary>
    /// The instance id of the scheduler behind this registration, or <see langword="null" /> when
    /// nothing has built one — or when the one that exists could not be asked.
    /// </summary>
    /// <remarks>
    /// Carried by the registration rather than read back off the scheduler, because reading it off a
    /// scheduler in another process is a round trip that blocks the thread doing it. This one was asked
    /// for asynchronously, under the same deadline <see cref="Status" /> was, so a listing costs at most
    /// one bounded wait however unreachable its targets are.
    /// </remarks>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// The attached store this scheduler is a window onto, or <see langword="null" /> for a scheduler
    /// of this process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set for, and only for, <see cref="SchedulerOrigin.Window" />. It is the name the application gave
    /// the store when it attached it — <c>AttachStore("prod", …)</c> — and together with
    /// <see cref="Name" /> it is the window's identity, spelled <c>prod/reporting</c> wherever one is
    /// shown. A scheduler of this process has no target and keeps its bare name, so nothing that
    /// existed before reads differently.
    /// </para>
    /// <para>
    /// The name is the scheduler's own, as the database spells it in <c>SCHED_NAME</c>, and it is what
    /// every member taking a scheduler name still wants: the target is how a reader says which database
    /// the name was found in, not a second half of the key.
    /// </para>
    /// </remarks>
    public string? Target { get; init; }

    /// <summary>
    /// Whether a scheduler exists under this name. A registration nothing has resolved yet reports
    /// <see langword="false" />; <see cref="Status" /> says what state a scheduler that does exist is in.
    /// </summary>
    public bool IsCreated => Status is not null;
}
