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

namespace Quartz.Core;

/// <summary>
/// Pauses a trigger whose retries ran out, saying why: what <c>PauseTriggerWhenRetriesExhausted()</c>
/// registers.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the scheduler changes: <see cref="ITriggerListener.TriggerRetriesExhausted" /> is raised
/// before the store completes the firing, and a completion that ends in no instruction leaves a paused
/// trigger paused. So the trigger stops at the occurrence that gave up, rather than going back to its
/// schedule and failing again, until an operator resumes it.
/// </para>
/// <para>
/// The reason is the exception's message, cut to <see cref="PauseDetails.MaxReasonLength" /> by the
/// store; the requester is <see cref="Requester" />, which is how a listing tells this pause from one an
/// operator made.
/// </para>
/// </remarks>
internal sealed class RetriesExhaustedPauseListener : ITriggerListener
{
    /// <summary>
    /// What the pause's <see cref="PauseDetails.RequestedBy" /> says: the scheduler, on its own account.
    /// </summary>
    internal const string Requester = "quartz:retries-exhausted";

    /// <summary>
    /// The listener's name, which is also what makes a second registration for the same scheduler a
    /// duplicate the listener manager refuses.
    /// </summary>
    internal const string ListenerName = "Quartz.PauseTriggerWhenRetriesExhausted";

    /// <inheritdoc />
    public string Name => ListenerName;

    /// <inheritdoc />
    public async ValueTask TriggerRetriesExhausted(
        ITrigger trigger,
        IJobExecutionContext context,
        JobExecutionException exception,
        CancellationToken cancellationToken = default)
    {
        PauseDetails details = new()
        {
            Reason = ReasonFor(exception),
            RequestedBy = Requester
        };

        await context.Scheduler.PauseTriggerWith(trigger.Key, details, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The message of what the job threw.
    /// </summary>
    /// <remarks>
    /// The run shell wraps an exception the job did not wrap itself twice over, and both wrappers say
    /// "Job threw an unhandled exception" — true, and no use as a reason. That one shape is looked through
    /// to the exception the job threw; a <see cref="JobExecutionException" /> the job threw itself is its
    /// own reason.
    /// </remarks>
    internal static string ReasonFor(JobExecutionException exception)
    {
        Exception cause = exception is { InnerException: JobExecutionProcessException { InnerException: { } thrown } }
            ? thrown
            : exception;

        return cause.Message;
    }
}
