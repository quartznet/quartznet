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
/// How the scheduler retries a failed firing of this job when the trigger that fired it has no
/// <see cref="ITrigger.RetryPolicy" /> of its own. Overrides the scheduler's default, which
/// <c>UseDefaultRetryPolicy</c> sets.
/// </summary>
/// <remarks>
/// <para>
/// Declared on the job rather than stored with it, for the reason <see cref="JobTimeoutAttribute" /> is:
/// whether a job is safe to retry is a property of its code, and an attribute travels with the type
/// through every job store, wire format and way of scheduling. Nothing is written to a trigger's row: the
/// policy is looked up each time a firing fails, so it covers triggers stored before it was declared.
/// It is inherited from a base class or from an interface the job implements, and the type's own
/// declaration wins over an interface's.
/// </para>
/// <para>
/// The constructors are the factories of <see cref="RetryPolicy" />, with each <see cref="TimeSpan" />
/// spelled as a string in its invariant form, because a <see cref="TimeSpan" /> cannot be an attribute
/// argument:
/// </para>
/// <list type="table">
/// <listheader><term>Attribute</term><description>Policy</description></listheader>
/// <item><term><c>[RetryPolicy(3, "00:05:00")]</c></term><description><see cref="RetryPolicy.Fixed" /></description></item>
/// <item><term><c>[RetryPolicy(5, "00:00:30", 2, MaxDelay = "00:10:00", Jitter = 0.2)]</c></term><description><see cref="RetryPolicy.Exponential(int, TimeSpan, double, TimeSpan?, double)" /></description></item>
/// <item><term><c>[RetryPolicy("00:00:10", "00:01:00", "01:00:00")]</c></term><description><see cref="RetryPolicy.Explicit" /></description></item>
/// <item><term><c>[RetryPolicy(0)]</c></term><description><see cref="RetryPolicy.None" />: never retried, whatever the scheduler's default</description></item>
/// </list>
/// <para>
/// <strong>An argument that is not a policy is refused.</strong> The attribute throws as it is read, and
/// the scheduler reads it when the job is added or scheduled, so <c>AddJob</c> and <c>ScheduleJob</c>
/// fail with a <see cref="SchedulerException" /> naming the job type.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [RetryPolicy(5, "00:00:30", 2, MaxDelay = "00:10:00")]
/// public sealed class ImportJob : IJob
/// {
///     public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
///     {
///         await Import(cancellationToken);
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="RetryPolicy" />
/// <seealso cref="JobTimeoutAttribute" />
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class RetryPolicyAttribute : Attribute
{
    // What the exponential constructor was given, kept so that MaxDelay and Jitter can rebuild the
    // policy around them: an attribute's named arguments are assigned after its constructor has run.
    private readonly bool exponential;
    private readonly TimeSpan initialDelay;
    private readonly double factor;
    private TimeSpan? maxDelay;
    private double jitter;

    /// <summary>
    /// Declares that the job is never retried: <see cref="RetryPolicy.None" />.
    /// </summary>
    /// <param name="maxAttempts"><c>0</c>. Any other count needs a delay.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts" /> is not <c>0</c>.</exception>
    public RetryPolicyAttribute(int maxAttempts)
    {
        if (maxAttempts != 0)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(maxAttempts),
                string.Create(CultureInfo.InvariantCulture, $"[RetryPolicy({maxAttempts})] names no delay. Write [RetryPolicy({maxAttempts}, \"00:01:00\")] for a fixed wait, or [RetryPolicy(0)] for a job that is never retried."));
        }

        MaxAttempts = 0;
        Policy = RetryPolicy.None;
    }

    /// <summary>
    /// Declares <see cref="RetryPolicy.Fixed" />: the same wait before every retry.
    /// </summary>
    /// <param name="maxAttempts">How many times to retry after the first failure; at least one.</param>
    /// <param name="delay">The wait before each retry, as an invariant <see cref="TimeSpan" />: <c>"00:05:00"</c> is five minutes.</param>
    /// <exception cref="ArgumentException"><paramref name="delay" /> is not a <see cref="TimeSpan" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxAttempts" /> is less than one, or <paramref name="delay" /> is negative.
    /// </exception>
    public RetryPolicyAttribute(int maxAttempts, string delay)
    {
        EnsureAttempts(maxAttempts);

        MaxAttempts = maxAttempts;
        Policy = RetryPolicy.Fixed(maxAttempts, ParseDelay(delay, nameof(delay)));
    }

    /// <summary>
    /// Declares <see cref="RetryPolicy.Exponential(int, TimeSpan, double, TimeSpan?, double)" />: a wait
    /// that grows by <paramref name="factor" /> with every retry. <see cref="MaxDelay" /> and
    /// <see cref="Jitter" /> are its other two arguments.
    /// </summary>
    /// <param name="maxAttempts">How many times to retry after the first failure; at least one.</param>
    /// <param name="initialDelay">The wait before the first retry, as an invariant <see cref="TimeSpan" />.</param>
    /// <param name="factor">What each wait is multiplied by to get the next one; at least <c>1</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="initialDelay" /> is not a <see cref="TimeSpan" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxAttempts" /> is less than one, <paramref name="initialDelay" /> is negative, or
    /// <paramref name="factor" /> is less than one or not a number.
    /// </exception>
    public RetryPolicyAttribute(int maxAttempts, string initialDelay, double factor)
    {
        EnsureAttempts(maxAttempts);

        exponential = true;
        this.initialDelay = ParseDelay(initialDelay, nameof(initialDelay));
        this.factor = factor;

        MaxAttempts = maxAttempts;
        Policy = RetryPolicy.Exponential(maxAttempts, this.initialDelay, factor, maxDelay: null, jitter: 0);
    }

    /// <summary>
    /// Declares <see cref="RetryPolicy.Explicit" />: the given waits, in order, one per retry.
    /// </summary>
    /// <param name="delays">
    /// The wait before each retry, each an invariant <see cref="TimeSpan" />; at least one, none negative.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="delays" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="delays" /> is empty, holds something that is not a <see cref="TimeSpan" /> or a
    /// negative one, or is too long for the triggers table's retry policy column.
    /// </exception>
    public RetryPolicyAttribute(params string[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);

        TimeSpan[] parsed = new TimeSpan[delays.Length];
        for (int i = 0; i < delays.Length; i++)
        {
            parsed[i] = ParseDelay(delays[i], nameof(delays));
        }

        Policy = RetryPolicy.Explicit(parsed);
        MaxAttempts = Policy.MaxAttempts;
    }

    /// <summary>
    /// How many times the job is retried after the first failure: <c>0</c> for <c>[RetryPolicy(0)]</c>,
    /// and the number of delays for the explicit form.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The policy the attribute declares.
    /// </summary>
    public RetryPolicy Policy { get; private set; }

    /// <summary>
    /// The ceiling every computed wait is clamped to, as an invariant <see cref="TimeSpan" />, or
    /// <see langword="null" /> to let them grow unbounded. Only the exponential form takes one.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a <see cref="TimeSpan" />, or the attribute is not the exponential form.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is shorter than the initial delay.</exception>
    public string? MaxDelay
    {
        get;
        init
        {
            EnsureExponential(nameof(MaxDelay));

            maxDelay = value is null ? null : ParseDelay(value, nameof(MaxDelay));
            Policy = RetryPolicy.Exponential(MaxAttempts, initialDelay, factor, maxDelay, jitter);
            field = value;
        }
    }

    /// <summary>
    /// How far either side of each computed wait a retry may land, as a fraction of it between <c>0</c>
    /// and <c>1</c>. Only the exponential form takes one.
    /// </summary>
    /// <exception cref="ArgumentException">The attribute is not the exponential form.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside <c>[0, 1]</c>.</exception>
    public double Jitter
    {
        get;
        init
        {
            EnsureExponential(nameof(Jitter));

            jitter = value;
            Policy = RetryPolicy.Exponential(MaxAttempts, initialDelay, factor, maxDelay, jitter);
            field = value;
        }
    }

    private void EnsureExponential(string property)
    {
        if (!exponential)
        {
            Throw.ArgumentException(
                $"{property} belongs to an exponential policy, which is [RetryPolicy(maxAttempts, initialDelay, factor)]; this one is {Policy}.",
                property);
        }
    }

    private static void EnsureAttempts(int maxAttempts)
    {
        if (maxAttempts == 0)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "A retry policy retries at least once. Write [RetryPolicy(0)] for a job that is never retried.");
        }
    }

    private static TimeSpan ParseDelay(string value, string paramName)
    {
        if (value is null)
        {
            Throw.ArgumentNullException(paramName);
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            Throw.ArgumentException(
                $"'{value}' is not a TimeSpan. Spell a retry delay the way TimeSpan does, invariantly: \"00:00:30\" for thirty seconds, \"1.00:00:00\" for a day.",
                paramName);
        }

        return parsed;
    }
}
