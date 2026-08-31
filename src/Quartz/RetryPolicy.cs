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

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Quartz;

/// <summary>
/// How often, and how far apart, the scheduler re-fires a trigger whose job has failed.
/// </summary>
/// <remarks>
/// <para>
/// A policy is a value: two policies of the same shape that would produce the same waits are equal.
/// There is no public constructor — <see cref="Fixed" />, <see cref="Exponential" /> and
/// <see cref="Explicit" /> are the only ways to make one, so a policy that could not be honoured
/// (no attempts, a negative wait, a backoff that shrinks) cannot be built at all.
/// </para>
/// <para>
/// <see cref="MaxAttempts" /> counts retries <i>after</i> the first failure, not fires: a trigger
/// carrying <c>Fixed(2, …)</c> whose job keeps throwing runs three times in total and is then back
/// on its ordinary schedule.
/// </para>
/// <para>
/// A retry never displaces the trigger's own next occurrence. The scheduler drops a retry that
/// would land at (or within a second of) the regular next fire time and lets the schedule win, so
/// a policy whose waits are longer than the gap between occurrences quietly does nothing.
/// </para>
/// </remarks>
public sealed class RetryPolicy : IEquatable<RetryPolicy>
{
    /// <summary>
    /// Which of the three factories made a policy. Carried rather than inferred from the numbers: a
    /// policy's shape is a decision its author made, and working it back out would mean asking
    /// whether a <see cref="double" /> is exactly one.
    /// </summary>
    private enum Shape
    {
        Fixed,
        Exponential,
        Explicit,
    }

    /// <summary>
    /// The field separator in the stored form. Neither the round-trip (<c>"R"</c>) form of a
    /// <see cref="double" /> nor the constant (<c>"c"</c>) form of a <see cref="TimeSpan" /> can
    /// contain it, so the stored form splits unambiguously.
    /// </summary>
    private const char Separator = ';';

    private const string FixedMarker = "fixed";
    private const string ExponentialMarker = "exp";
    private const string ExplicitMarker = "list";

    /// <summary>
    /// How many characters of stored form the triggers table can hold: <c>RETRY_POLICY</c> is 250
    /// characters wide in every dialect. Only <see cref="Explicit" /> can produce a longer one, and
    /// it rejects the policy rather than letting the insert truncate it.
    /// </summary>
    internal const int MaxStoredLength = 250;

    private readonly Shape shape;
    private readonly string storedForm;

    /// <summary>
    /// Creates a policy from its parts. Callers are the three factories, which have validated the
    /// parts already.
    /// </summary>
    private RetryPolicy(Shape shape, int maxAttempts, TimeSpan initialDelay, double backoffFactor, TimeSpan? maxDelay, ImmutableArray<TimeSpan> delays)
    {
        this.shape = shape;
        MaxAttempts = maxAttempts;
        InitialDelay = initialDelay;
        BackoffFactor = backoffFactor;
        MaxDelay = maxDelay;
        Delays = delays;
        storedForm = BuildStoredForm(shape, maxAttempts, initialDelay, backoffFactor, maxDelay, delays);
    }

    /// <summary>
    /// How many times the scheduler retries after the first failure. Always at least one.
    /// </summary>
    /// <remarks>
    /// This is the authoritative count: an <see cref="Explicit" /> policy's is the length of its
    /// delay table, and once the attempts are spent the trigger returns to its ordinary schedule
    /// rather than going into an error state.
    /// </remarks>
    public int MaxAttempts { get; }

    /// <summary>
    /// The wait before the first retry. For an <see cref="Explicit" /> policy this is the first
    /// entry of <see cref="Delays" />.
    /// </summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>
    /// What each wait is multiplied by to get the next one. <c>1</c> means a fixed wait.
    /// </summary>
    /// <remarks>
    /// Persisted with the round-trip (<c>"R"</c>) format and the invariant culture, so a policy
    /// written on one machine reads back bit for bit on another.
    /// </remarks>
    public double BackoffFactor { get; }

    /// <summary>
    /// The ceiling every computed wait is clamped to, or <see langword="null" /> when the backoff
    /// grows unbounded.
    /// </summary>
    public TimeSpan? MaxDelay { get; }

    /// <summary>
    /// The explicit table of waits, longest-lived entry last; empty when the waits are computed
    /// from <see cref="InitialDelay" /> and <see cref="BackoffFactor" />.
    /// </summary>
    /// <remarks>
    /// A table shorter than the number of attempts cannot happen — <see cref="MaxAttempts" /> is
    /// its length — but the last entry repeats for any attempt beyond it, so the property is safe
    /// to index through <see cref="DelayFor" /> alone.
    /// </remarks>
    public ImmutableArray<TimeSpan> Delays { get; }

    /// <summary>
    /// A policy that waits the same amount of time before every retry.
    /// </summary>
    /// <param name="maxAttempts">How many times to retry after the first failure; at least one.</param>
    /// <param name="delay">The wait before each retry; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxAttempts" /> is less than one, or <paramref name="delay" /> is negative.
    /// </exception>
    public static RetryPolicy Fixed(int maxAttempts, TimeSpan delay)
    {
        EnsureAttempts(maxAttempts, nameof(maxAttempts));
        EnsureNotNegative(delay, nameof(delay));

        return new RetryPolicy(Shape.Fixed, maxAttempts, delay, backoffFactor: 1, maxDelay: null, delays: ImmutableArray<TimeSpan>.Empty);
    }

    /// <summary>
    /// A policy whose wait grows by a constant factor with every retry.
    /// </summary>
    /// <param name="maxAttempts">How many times to retry after the first failure; at least one.</param>
    /// <param name="initialDelay">The wait before the first retry; not negative.</param>
    /// <param name="factor">
    /// What each wait is multiplied by to get the next one. At least <c>1</c>, which makes the
    /// policy wait the same amount every time; a shrinking backoff is not a backoff.
    /// </param>
    /// <param name="maxDelay">
    /// A ceiling for the computed waits, or <see langword="null" /> to let them grow unbounded.
    /// Not shorter than <paramref name="initialDelay" />, which a ceiling below the first wait
    /// would silently undo.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxAttempts" /> is less than one, <paramref name="initialDelay" /> is
    /// negative, <paramref name="factor" /> is less than one or is not a number, or
    /// <paramref name="maxDelay" /> is shorter than <paramref name="initialDelay" />.
    /// </exception>
    /// <remarks>
    /// A factor of <c>1</c> is legal and produces the same waits as <see cref="Fixed" />, but it is
    /// a different value: it says the trigger's author chose a backoff and set its rate to one,
    /// which is a thing to change rather than a fixed wait spelled the long way.
    /// </remarks>
    public static RetryPolicy Exponential(int maxAttempts, TimeSpan initialDelay, double factor = 2, TimeSpan? maxDelay = null)
    {
        EnsureAttempts(maxAttempts, nameof(maxAttempts));
        EnsureNotNegative(initialDelay, nameof(initialDelay));

        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 1)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(factor),
                "A backoff factor is a finite number of at least 1, not " + factor.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        if (maxDelay is not null && maxDelay.Value < initialDelay)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(maxDelay),
                "A maximum delay of " + FormatDelay(maxDelay.Value) + " is shorter than the initial delay of " + FormatDelay(initialDelay)
                + ", which would make every wait the maximum.");
        }

        return new RetryPolicy(Shape.Exponential, maxAttempts, initialDelay, factor, maxDelay, ImmutableArray<TimeSpan>.Empty);
    }

    /// <summary>
    /// A policy that waits the given amounts, in order. <see cref="MaxAttempts" /> is the number of
    /// waits given.
    /// </summary>
    /// <param name="delays">
    /// The wait before each retry, none of them negative and at least one of them present.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="delays" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="delays" /> is empty, holds a negative wait, or is long enough that the
    /// stored form would not fit the triggers table's 250-character retry policy column.
    /// </exception>
    public static RetryPolicy Explicit(params IReadOnlyList<TimeSpan> delays)
    {
        if (delays is null)
        {
            Throw.ArgumentNullException(nameof(delays));
        }

        if (delays.Count == 0)
        {
            Throw.ArgumentException("A retry policy needs at least one delay.", nameof(delays));
        }

        ImmutableArray<TimeSpan>.Builder builder = ImmutableArray.CreateBuilder<TimeSpan>(delays.Count);
        for (int i = 0; i < delays.Count; i++)
        {
            if (delays[i] < TimeSpan.Zero)
            {
                Throw.ArgumentOutOfRangeException(
                    nameof(delays),
                    string.Create(CultureInfo.InvariantCulture, $"Retry delay {i + 1} of {delays.Count} is negative ({FormatDelay(delays[i])}); a retry cannot be scheduled into the past."));
            }

            builder.Add(delays[i]);
        }

        RetryPolicy policy = new RetryPolicy(Shape.Explicit, delays.Count, delays[0], backoffFactor: 1, maxDelay: null, builder.MoveToImmutable());

        if (policy.storedForm.Length > MaxStoredLength)
        {
            Throw.ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"A retry policy with {delays.Count} delays does not fit the {MaxStoredLength}-character retry policy column (it would need {policy.storedForm.Length}). Use fewer delays, or an exponential policy."),
                nameof(delays));
        }

        return policy;
    }

    /// <summary>
    /// The wait before the given retry.
    /// </summary>
    /// <param name="attempt">
    /// Which retry to compute the wait for, counting from one: <c>1</c> is the wait after the first
    /// failure.
    /// </param>
    /// <returns>
    /// The wait, clamped to <see cref="MaxDelay" /> when there is one. An attempt beyond
    /// <see cref="MaxAttempts" /> answers as the last one does rather than throwing, because the
    /// decision to stop retrying belongs to the scheduler and not to the arithmetic.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempt" /> is less than one.</exception>
    /// <remarks>
    /// A wait too long to be represented is not an error. The arithmetic saturates, and the scheduler
    /// treats a retry it cannot express an instant for the way it treats one that would land on top of
    /// the next occurrence: there is no room for it, so the occurrence settles and the trigger keeps its
    /// ordinary schedule. Capping the waits when a policy is built or parsed instead would refuse a
    /// stored form that <see cref="ToStoredString" /> had just written, and would refuse a policy over
    /// attempts no trigger will reach.
    /// </remarks>
    public TimeSpan DelayFor(int attempt)
    {
        if (attempt < 1)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(attempt),
                string.Create(CultureInfo.InvariantCulture, $"Retry attempts count from 1; {attempt} is not an attempt."));
        }

        switch (shape)
        {
            case Shape.Fixed:
                return InitialDelay;

            case Shape.Explicit:
                return Delays[Math.Min(attempt, Delays.Length) - 1];

            default:
                // Ticks as a double: an exponential policy left running long enough overflows a
                // TimeSpan long before it overflows a double, and saturating at TimeSpan.MaxValue is
                // the honest answer for a wait nothing will ever come back from.
                double ticks = InitialDelay.Ticks * Math.Pow(BackoffFactor, attempt - 1);
                TimeSpan delay = ticks >= long.MaxValue ? TimeSpan.MaxValue : TimeSpan.FromTicks((long) ticks);

                if (MaxDelay is not null && delay > MaxDelay.Value)
                {
                    delay = MaxDelay.Value;
                }

                return delay;
        }
    }

    /// <summary>
    /// The policy as the triggers table holds it: a compact, culture-invariant string that starts
    /// with a marker naming the policy's shape.
    /// </summary>
    /// <returns>
    /// The stored form. Two policies that are <see cref="Equals(RetryPolicy)">equal</see> produce
    /// the same string, and <see cref="Parse" /> turns that string back into an equal policy.
    /// </returns>
    public string ToStoredString()
    {
        return storedForm;
    }

    /// <summary>
    /// Rebuilds a policy from the string <see cref="ToStoredString" /> produced.
    /// </summary>
    /// <param name="value">The stored form.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="value" /> is not a stored retry policy.</exception>
    public static RetryPolicy Parse(string value)
    {
        if (value is null)
        {
            Throw.ArgumentNullException(nameof(value));
        }

        if (!TryParseCore(value, out RetryPolicy? policy, out string? problem))
        {
            Throw.FormatException($"'{value}' is not a retry policy: {problem}");
        }

        return policy;
    }

    /// <summary>
    /// Rebuilds a policy from the string <see cref="ToStoredString" /> produced, answering whether
    /// the string was one.
    /// </summary>
    /// <param name="value">The stored form, which may be <see langword="null" /> or blank.</param>
    /// <param name="policy">The policy, or <see langword="null" /> when there was not one.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> was a stored retry policy.</returns>
    /// <remarks>
    /// A <see langword="null" /> or blank string answers <see langword="false" /> rather than
    /// throwing: that is what a trigger row with no retry policy reads as.
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out RetryPolicy? policy)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            policy = null;
            return false;
        }

        return TryParseCore(value, out policy, out _);
    }

    private static bool TryParseCore(string value, [NotNullWhen(true)] out RetryPolicy? policy, out string? problem)
    {
        string[] parts = value.Split(Separator);

        try
        {
            switch (parts[0])
            {
                case FixedMarker:
                    return TryParseFixed(parts, out policy, out problem);

                case ExponentialMarker:
                    return TryParseExponential(parts, out policy, out problem);

                case ExplicitMarker:
                    return TryParseExplicit(parts, out policy, out problem);

                default:
                    policy = null;
                    problem = $"'{parts[0]}' is not one of the policy shapes '{FixedMarker}', '{ExponentialMarker}' and '{ExplicitMarker}'";
                    return false;
            }
        }
        catch (ArgumentException ex)
        {
            // The factories own every rule about what a policy may say, so parsing goes through
            // them and reports what they refused rather than repeating their checks here.
            policy = null;
            problem = ex.Message;
            return false;
        }
    }

    private static bool TryParseFixed(string[] parts, [NotNullWhen(true)] out RetryPolicy? policy, out string? problem)
    {
        policy = null;

        if (parts.Length != 3)
        {
            problem = $"a fixed policy is '{FixedMarker}{Separator}<attempts>{Separator}<delay>'";
            return false;
        }

        if (!TryReadAttempts(parts[1], out int attempts) || !TryReadDelay(parts[2], out TimeSpan delay))
        {
            problem = "the attempt count or the delay is not a number";
            return false;
        }

        policy = Fixed(attempts, delay);
        problem = null;
        return true;
    }

    private static bool TryParseExponential(string[] parts, [NotNullWhen(true)] out RetryPolicy? policy, out string? problem)
    {
        policy = null;

        if (parts.Length is not (4 or 5))
        {
            problem = $"an exponential policy is '{ExponentialMarker}{Separator}<attempts>{Separator}<initial delay>{Separator}<factor>' with an optional '{Separator}<maximum delay>'";
            return false;
        }

        if (!TryReadAttempts(parts[1], out int attempts)
            || !TryReadDelay(parts[2], out TimeSpan initialDelay)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double factor))
        {
            problem = "the attempt count, the initial delay or the factor is not a number";
            return false;
        }

        TimeSpan? maxDelay = null;
        if (parts.Length == 5)
        {
            if (!TryReadDelay(parts[4], out TimeSpan parsedMaxDelay))
            {
                problem = "the maximum delay is not a duration";
                return false;
            }

            maxDelay = parsedMaxDelay;
        }

        policy = Exponential(attempts, initialDelay, factor, maxDelay);
        problem = null;
        return true;
    }

    private static bool TryParseExplicit(string[] parts, [NotNullWhen(true)] out RetryPolicy? policy, out string? problem)
    {
        policy = null;

        if (parts.Length < 2)
        {
            problem = $"an explicit policy is '{ExplicitMarker}' followed by at least one delay";
            return false;
        }

        TimeSpan[] delays = new TimeSpan[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
        {
            if (!TryReadDelay(parts[i], out delays[i - 1]))
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"delay {i} is not a duration");
                return false;
            }
        }

        policy = Explicit(delays);
        problem = null;
        return true;
    }

    /// <summary>
    /// The policy as the triggers table holds it, which is the whole of its state.
    /// </summary>
    public override string ToString()
    {
        return storedForm;
    }

    /// <summary>
    /// Whether the other policy has the same shape and would produce exactly the same waits, the
    /// same number of times.
    /// </summary>
    /// <param name="other">The policy to compare with.</param>
    /// <returns><see langword="true" /> when the two policies are the same value.</returns>
    /// <remarks>
    /// Written out by hand rather than left to a record: the delay table is a collection, and
    /// reference equality on it would make two policies built from the same numbers unequal — which
    /// the store contract tests compare through. The backoff factor is compared bit for bit, which
    /// is what round-tripping it through the <c>"R"</c> format guarantees and what "the same policy
    /// came back out of the column" means.
    /// </remarks>
    public bool Equals(RetryPolicy? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return shape == other.shape
               && MaxAttempts == other.MaxAttempts
               && InitialDelay == other.InitialDelay
               && BitConverter.DoubleToInt64Bits(BackoffFactor) == BitConverter.DoubleToInt64Bits(other.BackoffFactor)
               && Nullable.Equals(MaxDelay, other.MaxDelay)
               && Delays.AsSpan().SequenceEqual(other.Delays.AsSpan());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as RetryPolicy);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(shape);
        hash.Add(MaxAttempts);
        hash.Add(InitialDelay);
        hash.Add(BitConverter.DoubleToInt64Bits(BackoffFactor));
        hash.Add(MaxDelay);
        foreach (TimeSpan delay in Delays)
        {
            hash.Add(delay);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Whether two policies are the same value.
    /// </summary>
    /// <param name="left">The first policy.</param>
    /// <param name="right">The second policy.</param>
    /// <returns><see langword="true" /> when both are <see langword="null" /> or both are equal.</returns>
    public static bool operator ==(RetryPolicy? left, RetryPolicy? right) => left is null ? right is null : left.Equals(right);

    /// <summary>
    /// Whether two policies are different values.
    /// </summary>
    /// <param name="left">The first policy.</param>
    /// <param name="right">The second policy.</param>
    /// <returns><see langword="true" /> when the two are not the same value.</returns>
    public static bool operator !=(RetryPolicy? left, RetryPolicy? right) => !(left == right);

    private static string BuildStoredForm(Shape shape, int maxAttempts, TimeSpan initialDelay, double backoffFactor, TimeSpan? maxDelay, ImmutableArray<TimeSpan> delays)
    {
        string attempts = maxAttempts.ToString(CultureInfo.InvariantCulture);

        switch (shape)
        {
            case Shape.Fixed:
                return FixedMarker + Separator + attempts + Separator + FormatDelay(initialDelay);

            case Shape.Explicit:
                StringBuilder explicitForm = new StringBuilder(ExplicitMarker);
                foreach (TimeSpan delay in delays)
                {
                    explicitForm.Append(Separator).Append(FormatDelay(delay));
                }

                return explicitForm.ToString();

            default:
                string form = ExponentialMarker + Separator + attempts + Separator + FormatDelay(initialDelay)
                              + Separator + backoffFactor.ToString("R", CultureInfo.InvariantCulture);

                return maxDelay is null ? form : form + Separator + FormatDelay(maxDelay.Value);
        }
    }

    /// <summary>
    /// A duration in the constant (<c>"c"</c>) format, which is culture-independent by definition
    /// and keeps every tick.
    /// </summary>
    private static string FormatDelay(TimeSpan value)
    {
        return value.ToString("c", CultureInfo.InvariantCulture);
    }

    private static bool TryReadDelay(string text, out TimeSpan value)
    {
        return TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadAttempts(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void EnsureAttempts(int maxAttempts, string paramName)
    {
        if (maxAttempts < 1)
        {
            Throw.ArgumentOutOfRangeException(
                paramName,
                string.Create(CultureInfo.InvariantCulture, $"A retry policy retries at least once; {maxAttempts} attempts is not a policy."));
        }
    }

    private static void EnsureNotNegative(TimeSpan delay, string paramName)
    {
        if (delay < TimeSpan.Zero)
        {
            Throw.ArgumentOutOfRangeException(
                paramName,
                "A retry delay of " + FormatDelay(delay) + " is negative; a retry cannot be scheduled into the past.");
        }
    }
}
