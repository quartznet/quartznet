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
/// once for it, fired now and on time, not recovering, never retried or refired, with no calendar and a
/// token nothing cancels but an interrupt. The previous fire time is the trigger's own and the next is the
/// one its schedule gives after this firing, skipping what the calendar excludes, as a job store reports
/// them. There is no scheduler unless <see cref="WithScheduler" /> gives one, so a job that uses
/// <see cref="IJobExecutionContext.Scheduler" /> needs one — a fake is enough.
/// </para>
/// <para>
/// "Now" and <see cref="IJobExecutionContext.JobRunTime" /> are read from <see cref="TimeProvider.System" />
/// unless <see cref="WithTimeProvider" /> gives another clock. The scheduler is not asked for its clock: a
/// faked scheduler answers with a faked one. The run begins when the context is built.
/// </para>
/// <para>
/// The trigger is copied before it is used, as a job store copies the trigger it fires, so the trigger
/// passed to <see cref="WithTrigger" /> is not changed by the fire-instance id, the input, the retry
/// attempt or the recovery data the context is given, and one builder can build any number of
/// independent contexts.
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
    private bool recovering;
    private int? retryAttempt;
    private int refireCount;
    private ICalendar? calendar;
    private CancellationToken cancellationToken;
    private TimeProvider timeProvider = TimeProvider.System;

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
    /// Sets whether the firing recovers an execution a failed scheduler left behind, which is what
    /// <see cref="IJobExecutionContext.Recovering" /> answers.
    /// </summary>
    /// <remarks>
    /// A recovering context's trigger carries what a job store's recovery trigger carries: the
    /// <see cref="SchedulerConstants.FailedJobOriginalTriggerName" /> and
    /// <see cref="SchedulerConstants.FailedJobOriginalTriggerGroup" /> of the trigger it stands for, which
    /// <see cref="IJobExecutionContext.RecoveringTriggerKey" /> reads, and that firing's fire times as
    /// <see cref="SchedulerConstants.FailedJobOriginalTriggerFireTime" /> and
    /// <see cref="SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime" />. The trigger stands for
    /// itself, fired at the times this context reports. A trigger whose job data already names an
    /// original trigger keeps all four as it has them.
    /// </remarks>
    /// <param name="recovering">Whether the firing is a recovery; <see langword="true" /> if left out.</param>
    /// <seealso cref="JobBuilder{TJob}.RequestRecovery" />
    public JobExecutionContextBuilder Recovering(bool recovering = true)
    {
        this.recovering = recovering;
        return this;
    }

    /// <summary>
    /// Sets how many times the occurrence has already been retried, which is what
    /// <see cref="IJobExecutionContext.RetryAttempt" /> answers.
    /// </summary>
    /// <remarks>
    /// It is written to the copy of the trigger, where a job store writes it and where the context reads
    /// it. The last attempt is the one whose count equals the policy's
    /// <see cref="RetryPolicy.MaxAttempts" />; give the trigger or the job type a policy for
    /// <see cref="IJobExecutionContext.RetryPolicy" /> to answer one. Left out, the trigger's own count
    /// stands, which is <c>0</c> for a trigger that has never been retried.
    /// </remarks>
    /// <param name="retryAttempt"><c>0</c> on a regular fire, <c>n</c> on the <c>n</c>-th retry.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryAttempt" /> is negative.</exception>
    public JobExecutionContextBuilder WithRetryAttempt(int retryAttempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryAttempt);

        this.retryAttempt = retryAttempt;
        return this;
    }

    /// <summary>
    /// Sets how many times the job has already been re-run within this firing, which is what
    /// <see cref="IJobExecutionContext.RefireCount" /> answers.
    /// </summary>
    /// <remarks>
    /// A refire is the in-process loop a <see cref="JobExecutionException" /> with
    /// <see cref="JobExecutionException.RefireImmediately" /> asks for, not a retry.
    /// </remarks>
    /// <param name="refireCount"><c>0</c> on the first run, <c>n</c> on the <c>n</c>-th refire.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="refireCount" /> is negative.</exception>
    public JobExecutionContextBuilder WithRefireCount(int refireCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(refireCount);

        this.refireCount = refireCount;
        return this;
    }

    /// <summary>
    /// Sets the calendar the trigger fires against, which is what
    /// <see cref="IJobExecutionContext.Calendar" /> answers.
    /// </summary>
    /// <remarks>
    /// <see cref="IJobExecutionContext.NextFireTimeUtc" /> skips the times it excludes, as a trigger's
    /// <see cref="IOperableTrigger.Triggered" /> does. The trigger's
    /// <see cref="ITrigger.CalendarName" /> is left as it is.
    /// </remarks>
    /// <param name="calendar">The calendar, which is used as it is and not copied.</param>
    public JobExecutionContextBuilder WithCalendar(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        this.calendar = calendar;
        return this;
    }

    /// <summary>
    /// Sets a token that cancels <see cref="IJobExecutionContext.CancellationToken" />, so a test can
    /// cancel the job it runs.
    /// </summary>
    /// <remarks>
    /// The context's token is linked to it rather than being it: cancelling this token cancels the
    /// context's, and an interrupt cancels the context's and leaves this one alone. Hand the job
    /// <see cref="IJobExecutionContext.CancellationToken" />, as the scheduler does, so the token
    /// <see cref="IJob.Execute" /> is given cancels too.
    /// </remarks>
    /// <param name="cancellationToken">The token that cancels the firing.</param>
    public JobExecutionContextBuilder WithCancellation(CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    /// Sets the clock the context is built on: the default fire time is its "now", and
    /// <see cref="IJobExecutionContext.JobRunTime" /> is measured on it from the moment the context is
    /// built.
    /// </summary>
    /// <remarks>
    /// Pass the <c>FakeTimeProvider</c> a test drives, so <see cref="IJobExecutionContext.JobRunTime" />
    /// advances by exactly what the test advances it by. The default trigger is built on it too.
    /// </remarks>
    /// <param name="timeProvider">The clock, usually the one the scheduler under test is given.</param>
    public JobExecutionContextBuilder WithTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        return this;
    }

    /// <summary>
    /// Builds the context. Each call builds a new one, over a new copy of the trigger.
    /// </summary>
    /// <remarks>
    /// The context owns a <see cref="CancellationTokenSource" /> once its token has been read, or from the
    /// start when <see cref="WithCancellation" /> gave a token, so dispose it when the job has run.
    /// </remarks>
    public JobExecutionContextImpl Build()
    {
        DateTimeOffset firedAt = fireTimeUtc ?? timeProvider.GetUtcNow();
        DateTimeOffset dueAt = scheduledFireTimeUtc ?? firedAt;

        IJobDetail detail = jobDetail ?? JobBuilder.Create()
            .OfType(jobType)
            .WithIdentity(jobType.Type.Name)
            .Build();

        IOperableTrigger fired = trigger is not null
            ? (IOperableTrigger) trigger.Clone()
            : (IOperableTrigger) TriggerBuilder.Create(timeProvider)
                .ForJob(detail)
                .StartAt(dueAt)
                .Build();

        fired.FireInstanceId ??= Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        if (hasInput)
        {
            fired.JobDataMap[SchedulerConstants.JobInput] = input;
        }

        if (retryAttempt is { } attempt)
        {
            fired.RetryAttempt = attempt;
        }

        if (recovering && !fired.JobDataMap.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName))
        {
            fired.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerName] = fired.Key.Name;
            fired.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerGroup] = fired.Key.Group;
            fired.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerFireTime] = Convert.ToString(firedAt, CultureInfo.InvariantCulture);
            fired.JobDataMap[SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime] = Convert.ToString(dueAt, CultureInfo.InvariantCulture);
        }

        TriggerFiredBundle bundle = new()
        {
            JobDetail = detail,
            Trigger = fired,
            Calendar = calendar,
            Recovering = recovering,
            FireTimeUtc = firedAt,
            ScheduledFireTimeUtc = dueAt,
            PreviousFireTimeUtc = fired.PreviousFireTimeUtc,
            NextFireTimeUtc = NextFireTimeAfter(fired, calendar, dueAt),
        };

        JobExecutionContextImpl context = new(scheduler!, bundle, job, inputSerializer: null, timeProvider)
        {
            RefireCount = refireCount,
        };
        context.LinkCancellation(cancellationToken);
        context.StartRunClock();
        return context;
    }

    /// <summary>
    /// The trigger's next fire time after <paramref name="after" />, passing over what
    /// <paramref name="calendar" /> excludes.
    /// </summary>
    /// <remarks>
    /// The same walk a trigger's own <see cref="IOperableTrigger.Triggered" /> takes, ending at the same
    /// give-up year, so a calendar that never lets the trigger fire again ends the schedule rather than
    /// looping.
    /// </remarks>
    private static DateTimeOffset? NextFireTimeAfter(IOperableTrigger trigger, ICalendar? calendar, DateTimeOffset after)
    {
        DateTimeOffset? next = trigger.GetFireTimeAfter(after);
        while (calendar is not null && next is { } candidate && !calendar.IsTimeIncluded(candidate))
        {
            next = trigger.GetFireTimeAfter(candidate);
            if (next?.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                return null;
            }
        }

        return next;
    }
}
