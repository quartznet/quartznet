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

using System.Diagnostics;

namespace Quartz;

/// <summary>
/// Scheduler constants.
/// </summary>
/// <remarks>
/// The default group a job or trigger belongs to is <see cref="Key{T}.DefaultGroup" /> — spelled
/// <c>JobKey.DefaultGroup</c> or <c>TriggerKey.DefaultGroup</c> at a call site — which is where the
/// name of the thing it names lives.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public static class SchedulerConstants
{
    /// <summary>
    /// A constant <see cref="ITrigger" /> group name used internally by the
    /// scheduler - clients should not use the value of this constant
    /// ("RECOVERING_JOBS") for the name of a <see cref="ITrigger" />'s group.
    /// </summary>
    public const string DefaultRecoveryGroup = "RECOVERING_JOBS";

    /// <summary>
    /// A constant <see cref="ITrigger" /> group name used internally by the
    /// scheduler - clients should not use the value of this constant
    /// ("FAILED_OVER_JOBS") for the name of a <see cref="ITrigger" />'s group.
    /// </summary>
    public const string DefaultFailOverGroup = "FAILED_OVER_JOBS";

    /// <summary>
    ///  A constant <see cref="JobDataMap" /> key that can be used to retrieve the
    /// name of the original <see cref="ITrigger" /> from a recovery trigger's
    /// data map in the case of a job recovering after a failed scheduler
    /// instance.
    /// </summary>
    /// <seealso cref="IJobDetail.RequestsRecovery" />
    public const string FailedJobOriginalTriggerName = "QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME";

    /// <summary>
    /// A constant <see cref="JobDataMap" /> key that can be used to retrieve the
    /// group of the original <see cref="ITrigger" /> from a recovery trigger's
    /// data map in the case of a job recovering after a failed scheduler
    /// instance.
    /// </summary>
    /// <seealso cref="IJobDetail.RequestsRecovery" />
    public const string FailedJobOriginalTriggerGroup = "QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP";

    /// <summary>
    /// A constant <see cref="JobDataMap" /> key that can be used to retrieve the
    /// fire time of the original <see cref="ITrigger" /> from a recovery
    /// trigger's data map in the case of a job recovering after a failed scheduler
    /// instance.
    /// </summary>
    /// <remarks>
    /// Note that this is the time the original firing actually occurred,
    /// which may be different from the scheduled fire time - as a trigger doesn't
    /// always fire exactly on time.
    /// </remarks>
    /// <seealso cref="IJobDetail.RequestsRecovery" />
    public const string FailedJobOriginalTriggerFireTime = "QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_AS_STRING";

    /// <summary>
    /// A constant <code>JobDataMap</code> key that can be used to retrieve the scheduled
    /// fire time of the original <code>Trigger</code> from a recovery  trigger's data
    /// map in the case of a job recovering after a failed scheduler instance.
    /// </summary>
    /// <remarks>
    /// Note that this is the time the original firing was scheduled for, which may
    /// be different from the actual firing time - as a trigger doesn't always fire exactly on time.
    /// </remarks>
    public const string FailedJobOriginalTriggerScheduledFireTime = "QRTZ_FAILED_JOB_ORIG_TRIGGER_SCHEDULED_FIRETIME_AS_STRING";

    /// <summary>
    /// A special date time to check against when signaling scheduling change when the signaled fire date suggestion is actually irrelevant.
    /// We only want to signal the change.
    /// </summary>
    internal static DateTimeOffset? SchedulingSignalDateTime = new DateTimeOffset(1982, 6, 28, 0, 0, 0, TimeSpan.FromSeconds(0));

    /// <summary>
    /// Signals Quartz not to consider job data map as clean when deserialized - used in scenarios where data format needs to be converted.
    /// </summary>
    public const string ForceJobDataMapDirty = "QRTZ_FORCE_JOB_DATAMAP_DIRTY";

    /// <summary>
    /// The <see cref="JobDataMap" /> key an <see cref="IJob{TInput}" />'s input is carried under, on the
    /// job's own map or — overriding it — on the trigger's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stored value is always a <see cref="string" />: the scheduler serializes anything else with
    /// the scheduler's <see cref="Quartz.Extensibility.IJobInputSerializer" /> as the job or trigger is
    /// stored. A string is what survives every path the value can take afterwards — AdoJobStore's
    /// <c>StoreJobDataAsStrings</c> mode, the JSON write gate that refuses a job data value no reader
    /// can produce, the Newtonsoft serializer, the binary blob column and the HTTP wire — so the round
    /// trip does not depend on which of them the value went through.
    /// </para>
    /// <para>
    /// Set it with <c>UsingInput</c> on a job or trigger builder rather than by spelling this key.
    /// </para>
    /// </remarks>
    /// <seealso cref="IJob{TInput}" />
    /// <seealso cref="JobInputBuilderExtensions" />
    public const string JobInput = "QRTZ_JOB_INPUT";

    /// <summary>
    /// The <see cref="IJobDetail" /> group the one-call
    /// <see cref="SchedulerJobExtensions.ScheduleJob{TJob, TInput}(IScheduler, TInput, DateTimeOffset, OneOffJobOptions, CancellationToken)" />
    /// overloads keep their durable jobs in — one per job type, named after the type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clients should not build jobs of their own in this group: it is the scheduler's, and what is in
    /// it is one job per job type that the one-call overloads maintain. It is named here so that a
    /// dashboard, a query or a clean-up can say which jobs it means without spelling the string.
    /// </para>
    /// <para>
    /// Addressing the job that <em>is</em> in it — to point a trigger of one's own at it, or to look it
    /// up — is <see cref="ScheduledJobKey{TJob}" />, which spells the whole key so nothing has to
    /// re-derive it.
    /// </para>
    /// </remarks>
    public const string ScheduledJobGroup = "QRTZ_SCHEDULED";

    /// <summary>
    /// The key of the single durable job every firing of <typeparamref name="TJob" /> scheduled by the
    /// one-call <c>ScheduleJob&lt;TJob, TInput&gt;</c> overloads hangs off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One durable job per job type, named after the type and kept in
    /// <see cref="ScheduledJobGroup" />. Named here, beside the group it is in, because that group is
    /// reserved: an integration with a second scheduling path of its own — a recurring trigger built
    /// by hand, say, for the same job — needs to point that trigger at the job the one-liner ensures,
    /// and asking is better than re-deriving the key against the advice not to build jobs in the
    /// group.
    /// </para>
    /// <para>
    /// The key is derived from the type, not looked up, so it answers whether or not anything has been
    /// scheduled yet. The job itself appears in the store the first time one of the one-call overloads
    /// is used on a scheduler.
    /// </para>
    /// </remarks>
    /// <typeparam name="TJob">The job whose durable detail to name.</typeparam>
    /// <returns>The key of the durable job the one-call overloads ensure.</returns>
    /// <seealso cref="SchedulerJobExtensions" />
    public static JobKey ScheduledJobKey<TJob>() where TJob : IJob
    {
        return new JobKey(typeof(TJob).Name, ScheduledJobGroup);
    }

    /// <summary>
    /// The <see cref="JobDataMap" /> key carrying the W3C <c>traceparent</c> of the activity that
    /// scheduled a trigger, so that the firing can be linked back to it however much later it happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written onto the <em>trigger's</em> map by the scheduler, on every entry point that stores a
    /// trigger, whenever <see cref="Activity.Current" /> is a W3C activity and
    /// <see cref="QuartzSchedulerOptions.PropagateTraceContext" /> is on. Read back when the firing's
    /// span is opened, where it becomes an <see cref="ActivityLink" /> rather than a parent — a job
    /// scheduled for next Tuesday is its own trace, and a trace that stays open for a week is not a
    /// trace.
    /// </para>
    /// <para>
    /// A trigger scheduled outside an activity has the key <em>removed</em> rather than left alone,
    /// because the scheduler stores the trigger object it was handed rather than a copy of it: a trigger
    /// re-scheduled outside an activity would otherwise keep pointing at the trace that scheduled it the
    /// first time.
    /// </para>
    /// <para>
    /// This is the trigger's map, never the job's, so
    /// <see cref="PersistJobDataAfterExecutionAttribute" /> — which writes back only the job's map —
    /// never sees it and the two cannot interact.
    /// </para>
    /// </remarks>
    /// <seealso cref="TraceState" />
    public const string TraceParent = "QRTZ_TRACEPARENT";

    /// <summary>
    /// The <see cref="JobDataMap" /> key carrying the W3C <c>tracestate</c> that accompanies
    /// <see cref="TraceParent" />, present only when the scheduling activity had one.
    /// </summary>
    /// <seealso cref="TraceParent" />
    public const string TraceState = "QRTZ_TRACESTATE";
}