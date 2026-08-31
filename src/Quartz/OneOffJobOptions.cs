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
/// What the one-call <c>ScheduleJob&lt;TJob, TInput&gt;</c> overloads may be told about the single
/// firing they arrange, past the payload and the time.
/// </summary>
/// <remarks>
/// <para>
/// Every member is optional and every default is the one the equivalent builder call would have
/// produced, so <see langword="default" /> — what omitting the argument gives — is "a one-shot trigger
/// with a generated name, in this job type's own group".
/// </para>
/// <para>
/// Named for what the call creates rather than for the call: one off, one firing, one trigger. An
/// overload that schedules a <em>recurring</em> job would say something else — a schedule, an end time,
/// a calendar — and gets an options type of its own rather than nullable members here that mean
/// nothing to this one.
/// </para>
/// <para>
/// It is not <see cref="ScheduleJobOptions" />, whose one member says whether a store may over-write
/// what it already holds. That one describes a <em>store</em> operation and is what
/// <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, System.Threading.CancellationToken)" />
/// takes; this one describes the <em>trigger</em> the one-liner builds, and carries <see cref="Replace" />
/// so that it can pass it on.
/// </para>
/// </remarks>
/// <seealso cref="SchedulerJobExtensions" />
public readonly record struct OneOffJobOptions
{
    /// <summary>
    /// Schedule the firing under the given name, over-writing one already scheduled under it. The
    /// name for <c>new OneOffJobOptions { Name = name, Replace = true }</c>, which is what
    /// rescheduling a firing an application can name — a reminder, a saga step, a timeout — always
    /// says.
    /// </summary>
    /// <remarks>
    /// A preset rather than a bare <c>Replacing</c>, unlike <see cref="AddJobOptions.Replacing" /> and
    /// its siblings, because <see cref="Replace" /> alone would do nothing here: without a name the
    /// trigger gets a generated one, and a generated name has nothing to replace. The name is the
    /// whole of what makes replacing meaningful, so it is a parameter rather than something to
    /// remember to set afterwards.
    /// </remarks>
    /// <param name="name">The trigger's name, which is also the handle to cancel or replace it by.</param>
    public static OneOffJobOptions Replacing(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new OneOffJobOptions { Name = name, Replace = true };
    }

    /// <summary>
    /// The trigger's name. Defaults to a generated identifier, so two calls never collide by accident;
    /// give one when the firing has an identity of its own — a message id, a saga step — and it becomes
    /// the handle to <see cref="IScheduler.UnscheduleJob" /> or to replace with.
    /// </summary>
    /// <remarks>
    /// A name and a group rather than a <see cref="TriggerKey" />, which is what every other place
    /// that identifies a trigger takes. The two halves have different defaults — a generated name, a
    /// group named after the job type — and each is worth setting without the other: "call it
    /// <c>order-42</c>, wherever such firings go" and "put it in this saga's group, any name will
    /// do" are both ordinary, and a key can express neither. The key exists once the trigger does,
    /// and the call answers with it as <see cref="ScheduledOneOffJob.TriggerKey" />.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// The trigger's group. Defaults to the job type's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group is the correlation axis: everything scheduled for one saga, one tenant or one
    /// conversation can share a group and be listed, paused or unscheduled together.
    /// </para>
    /// <para>
    /// The default is a group of the job type's name rather than <see cref="Key{T}.DefaultGroup" />,
    /// which matters to anything that already has a trigger-key contract of its own: a caller that
    /// cancels with <c>new TriggerKey(id)</c> is naming the default group, so scheduling through these
    /// overloads without saying <c>Group = TriggerKey.DefaultGroup</c> puts the trigger somewhere that
    /// cancellation silently stops matching. Name the group the contract expects, and the two agree.
    /// </para>
    /// </remarks>
    public string? Group { get; init; }

    /// <summary>
    /// The trigger's description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The trigger's priority, which breaks ties when more triggers are due at once than the thread
    /// pool can run. Defaults to <see cref="TriggerConstants.DefaultPriority" />.
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// The execution group the firing counts against, when execution limits are in use.
    /// </summary>
    /// <seealso cref="ITrigger.ExecutionGroup" />
    public string? ExecutionGroup { get; init; }

    /// <summary>
    /// What to do when the scheduler was not running at the moment the firing was due. Defaults to
    /// <see cref="SimpleTriggerMisfireInstruction.SmartPolicy" />, which for a one-shot trigger means
    /// fire as soon as the scheduler is back.
    /// </summary>
    public SimpleTriggerMisfireInstruction? MisfireInstruction { get; init; }

    /// <summary>
    /// Whether a trigger already stored under the same key is over-written rather than reported as a
    /// conflict. Scheduling over an existing trigger is then one store operation under one lock — no
    /// <c>CheckExists</c> / <c>UnscheduleJob</c> / <c>ScheduleJob</c> for the caller to serialize.
    /// </summary>
    /// <remarks>
    /// Only meaningful together with <see cref="Name" />: a generated name has nothing to replace,
    /// which is why the preset that sets this is <see cref="Replacing" /> and takes the name.
    /// </remarks>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether the durable job the firings hang off is marked
    /// <see cref="IJobDetail.RequestsRecovery" />, so that a firing interrupted by a hard shutdown is
    /// re-executed when the scheduler comes back. Defaults to <see langword="false" />, which is
    /// <see cref="JobBuilder" />'s own default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one member here that describes the <em>job</em> rather than the trigger, because the job is
    /// the one thing the one-liner builds that a caller cannot otherwise reach — and recovery is a
    /// property of it, not of a firing.
    /// </para>
    /// <para>
    /// The job is ensured once per scheduler instance, so the first call's value wins for the process's
    /// lifetime: a later call asking for something else finds the job already there and does not store
    /// it again. That is how the memo already treats every other aspect of the job — its description,
    /// its durability, the type it names — and it is why this is a named boolean rather than a
    /// configuration delegate, which would look as though it varied per call.
    /// </para>
    /// <para>
    /// A process that has to change it restarts, or deletes the job
    /// <see cref="SchedulerConstants.ScheduledJobKey{TJob}" /> names — the next call finds it gone
    /// and stores it afresh with whatever that call asked for.
    /// </para>
    /// </remarks>
    public bool RequestRecovery { get; init; }
}
