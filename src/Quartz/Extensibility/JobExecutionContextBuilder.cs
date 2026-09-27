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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Quartz.Impl;

namespace Quartz.Extensibility;

/// <summary>
/// Builds the execution context a job's <see cref="IJob.Execute" /> is handed, so that a job can be
/// called directly — in a unit test, most usually — without a scheduler firing it.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobExecutionContextBuilder.For(job).WithJob(detail).WithTrigger(trigger).FiredAt(when).Build()</c>
/// builds the context Quartz itself builds, a <see cref="JobExecutionContextImpl" />, and every part of
/// that chain but <see cref="For{TJob}" /> and <see cref="Build" /> can be left out.
/// </para>
/// <para>
/// What is left out is what a firing would have: a job detail of the job's own type, a trigger that fires
/// once for it, fired now and on time, not recovering. The previous fire time is the trigger's own and
/// the next is the one its schedule gives after this firing, as a job store reports them. There is no
/// scheduler unless <see cref="WithScheduler" /> gives one, so a job that uses
/// <see cref="IJobExecutionContext.Scheduler" /> needs one — a fake is enough.
/// </para>
/// <para>
/// The trigger is copied before it is used, as a job store copies the trigger it fires, so the trigger
/// passed to <see cref="WithTrigger" /> is not changed by the fire-instance id or the input the context
/// is given, and one builder can build any number of independent contexts.
/// </para>
/// </remarks>
/// <seealso cref="TriggerFireTimes" />
public sealed class JobExecutionContextBuilder
{
    private readonly IJob job;
    private readonly JobType jobType;

    private IJobDetail? jobDetail;
    private IOperableTrigger? trigger;
    private IScheduler? scheduler;
    private DateTimeOffset? fireTimeUtc;
    private DateTimeOffset? scheduledFireTimeUtc;
    private bool hasInput;
    private object? input;

    private JobExecutionContextBuilder(IJob job, JobType jobType)
    {
        this.job = job;
        this.jobType = jobType;
    }

    /// <summary>
    /// Starts a context for one execution of <paramref name="job" />.
    /// </summary>
    /// <remarks>
    /// Generic so that the job detail made when <see cref="WithJob" /> is not called names the job's
    /// type without reflecting over the instance, which a trimmed application could not do.
    /// </remarks>
    /// <typeparam name="TJob">The job's type, which the default job detail is made for.</typeparam>
    /// <param name="job">The instance whose <see cref="IJob.Execute" /> the context is for.</param>
    public static JobExecutionContextBuilder For<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(TJob job) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(job);

        return new JobExecutionContextBuilder(job, new JobType(typeof(TJob)));
    }

    /// <summary>
    /// Sets the job detail the context carries: its key, its job data, its flags.
    /// </summary>
    /// <param name="jobDetail">The job detail, which is used as it is and not copied.</param>
    public JobExecutionContextBuilder WithJob(IJobDetail jobDetail)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

        this.jobDetail = jobDetail;
        return this;
    }

    /// <summary>
    /// Sets the trigger that fired, whose job data overrides the job detail's in
    /// <see cref="IJobExecutionContext.MergedJobDataMap" />.
    /// </summary>
    /// <param name="trigger">The trigger, which is copied when the context is built.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="trigger" /> is not an <see cref="IOperableTrigger" />, which every trigger a job
    /// store fires is.
    /// </exception>
    public JobExecutionContextBuilder WithTrigger(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger is not IOperableTrigger operable)
        {
            return Throw.ArgumentException<JobExecutionContextBuilder>(
                $"{trigger.GetType()} does not implement IOperableTrigger, which every trigger a job store fires does. "
                + "Derive the trigger from Quartz.Impl.Triggers.TriggerBase.",
                nameof(trigger));
        }

        this.trigger = operable;
        return this;
    }

    /// <summary>
    /// Sets what <see cref="IJobExecutionContext.Scheduler" /> answers.
    /// </summary>
    /// <param name="scheduler">The scheduler, usually a fake one.</param>
    public JobExecutionContextBuilder WithScheduler(IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        this.scheduler = scheduler;
        return this;
    }

    /// <summary>
    /// Sets when the trigger fired and, for a late firing, when it was due.
    /// </summary>
    /// <param name="fireTimeUtc">What <see cref="IJobExecutionContext.FireTimeUtc" /> answers.</param>
    /// <param name="scheduledFireTimeUtc">
    /// What <see cref="IJobExecutionContext.ScheduledFireTimeUtc" /> answers. Left out, the firing was
    /// on time and it is <paramref name="fireTimeUtc" />.
    /// </param>
    public JobExecutionContextBuilder FiredAt(DateTimeOffset fireTimeUtc, DateTimeOffset? scheduledFireTimeUtc = null)
    {
        this.fireTimeUtc = fireTimeUtc;
        this.scheduledFireTimeUtc = scheduledFireTimeUtc;
        return this;
    }

    /// <summary>
    /// Sets the input the firing carries, which an <see cref="IJob{TInput}" /> is handed and
    /// <see cref="JobExecutionContextInputExtensions.GetInput{TInput}" /> reads.
    /// </summary>
    /// <remarks>
    /// It is put on the copy of the trigger, so it wins over an input on the job detail, as a trigger's
    /// input does when a scheduler fires it. It is stored as the value itself rather than serialized,
    /// which is how a job reads it back without an <see cref="IJobInputSerializer" />.
    /// </remarks>
    /// <typeparam name="TInput">The type the job reads the input as.</typeparam>
    /// <param name="input">The payload.</param>
    public JobExecutionContextBuilder WithInput<TInput>(TInput input)
    {
        this.input = input;
        hasInput = true;
        return this;
    }

    /// <summary>
    /// Builds the context. Each call builds a new one, over a new copy of the trigger.
    /// </summary>
    /// <remarks>
    /// The context owns a <see cref="CancellationTokenSource" /> once its token has been read, so dispose
    /// it when the job has run.
    /// </remarks>
    public JobExecutionContextImpl Build()
    {
        DateTimeOffset firedAt = fireTimeUtc ?? TimeProvider.System.GetUtcNow();
        DateTimeOffset dueAt = scheduledFireTimeUtc ?? firedAt;

        IJobDetail detail = jobDetail ?? JobBuilder.Create()
            .OfType(jobType)
            .WithIdentity(jobType.Type.Name)
            .Build();

        IOperableTrigger fired = trigger is not null
            ? (IOperableTrigger) trigger.Clone()
            : (IOperableTrigger) TriggerBuilder.Create()
                .ForJob(detail)
                .StartAt(dueAt)
                .Build();

        fired.FireInstanceId ??= Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        if (hasInput)
        {
            fired.JobDataMap[SchedulerConstants.JobInput] = input;
        }

        TriggerFiredBundle bundle = new()
        {
            JobDetail = detail,
            Trigger = fired,
            Recovering = false,
            FireTimeUtc = firedAt,
            ScheduledFireTimeUtc = dueAt,
            PreviousFireTimeUtc = fired.PreviousFireTimeUtc,
            NextFireTimeUtc = fired.GetFireTimeAfter(dueAt),
        };

        return new JobExecutionContextImpl(scheduler!, bundle, job);
    }
}
