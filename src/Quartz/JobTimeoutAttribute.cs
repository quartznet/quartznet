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

using System.Globalization;

namespace Quartz;

/// <summary>
/// How long a firing of this job may run before the scheduler signals cancellation to it. Read by the
/// timeout middleware <c>AddJobTimeout</c> registers, and overrides that call's scheduler-wide default.
/// </summary>
/// <remarks>
/// <para>
/// Declared on the job rather than stored with it, for the reason
/// <see cref="DisallowConcurrentExecutionAttribute" /> is: how long the work may take is a property of
/// the code that does it, and an attribute travels with the type through every job store, every wire
/// format and every way of scheduling — where a value written into a detail would have to be persisted,
/// migrated and round-tripped to reach the same places. It is inherited from a base class or from an
/// interface the job implements, so a contract can set the budget for everything that fulfils it.
/// </para>
/// <para>
/// <strong>Nothing happens without <c>AddJobTimeout</c>.</strong> The attribute is read by the timeout
/// middleware, and a scheduler with no middleware has no timeouts. Call <c>q.AddJobTimeout()</c> to
/// register it with no scheduler-wide default, so that only the jobs carrying this attribute are
/// bounded, or <c>q.AddJobTimeout(TimeSpan)</c> to bound every job and let this attribute vary it.
/// </para>
/// <para>
/// <strong>A budget of zero means no timeout.</strong> <c>[JobTimeout("00:00:00")]</c> exempts a job
/// from a scheduler-wide default — the long-running one whose whole point is to run until it is done.
/// A negative budget is refused.
/// </para>
/// <para>
/// <strong>A job that ignores its <see cref="System.Threading.CancellationToken" /> cannot be
/// stopped.</strong> The timeout signals cancellation the way an operator's
/// <see cref="IScheduler.InterruptFireInstance" /> does, and nothing in .NET can abort code that
/// declines to notice; <c>CA2016</c> is what polices forwarding the token. Such a job runs to
/// completion and is then reported as having timed out.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [JobTimeout("00:00:30")]
/// public sealed class ReportJob : IJob
/// {
///     public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
///     {
///         await BuildReport(cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IJobExecutionMiddleware" />
/// <seealso cref="DisallowConcurrentExecutionAttribute" />
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class JobTimeoutAttribute : Attribute
{
    /// <summary>
    /// Declares the budget as a <see cref="TimeSpan" /> in its invariant form — <c>"00:05:00"</c> is
    /// five minutes, <c>"1.00:00:00"</c> is a day.
    /// </summary>
    /// <remarks>
    /// A string, because <see cref="TimeSpan" /> is not something an attribute argument can be. The
    /// format is <see cref="TimeSpan.Parse(string, IFormatProvider)" />'s, parsed with the invariant
    /// culture so the same source reads the same everywhere, and a value it cannot parse is refused
    /// here rather than silently meaning nothing.
    /// </remarks>
    /// <param name="timeout">
    /// The budget, as an invariant <see cref="TimeSpan" />. <c>"00:00:00"</c> means this job has no
    /// timeout, whatever the scheduler's default is.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="timeout" /> is not a <see cref="TimeSpan" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout" /> is negative.</exception>
    public JobTimeoutAttribute(string timeout)
    {
        ArgumentNullException.ThrowIfNull(timeout);

        if (!TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            Throw.ArgumentException(
                $"'{timeout}' is not a TimeSpan. Spell the job's timeout the way TimeSpan does, invariantly: \"00:05:00\" for five minutes, \"1.00:00:00\" for a day.",
                nameof(timeout));
        }

        if (parsed < TimeSpan.Zero)
        {
            Throw.ArgumentOutOfRangeException(nameof(timeout), $"A job's timeout cannot be negative, and '{timeout}' is. Use \"00:00:00\" to say the job has no timeout.");
        }

        Timeout = parsed;
    }

    /// <summary>
    /// The budget a firing of this job gets, or <see cref="TimeSpan.Zero" /> when it has none.
    /// </summary>
    public TimeSpan Timeout { get; }
}
