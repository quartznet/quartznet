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
/// What a pause says about itself: why it was made, and who asked for it. The argument of the
/// <c>*With</c> pause members, such as <see cref="IScheduler.PauseTriggerWith" />.
/// </summary>
/// <remarks>
/// <para>
/// When the pause was made is not here: the store stamps it from the scheduler's own clock, and
/// answers all three on <see cref="PauseInfo" />.
/// </para>
/// <para>
/// A context object rather than two parameters, so the next thing a pause carries is a property
/// rather than an overload.
/// </para>
/// </remarks>
public sealed class PauseDetails
{
    /// <summary>
    /// The longest <see cref="Reason" /> a store keeps, in UTF-16 code units. A longer one is cut to this,
    /// which is what the ADO.NET store's <c>PAUSE_REASON</c> column holds.
    /// </summary>
    public const int MaxReasonLength = 250;

    /// <summary>
    /// The longest <see cref="RequestedBy" /> a store keeps, in UTF-16 code units. A longer one is cut to
    /// this, which is what the ADO.NET store's <c>PAUSED_BY</c> column holds.
    /// </summary>
    public const int MaxRequestedByLength = 200;

    /// <summary>
    /// A pause that says nothing about itself, which is what the reasonless members record.
    /// </summary>
    internal static readonly PauseDetails None = new();

    /// <summary>
    /// Why the pause was made, or <see langword="null" /> when the caller did not say. Blank reads as
    /// <see langword="null" />.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Who asked for the pause — a user name, a service, or <c>quartz:retries-exhausted</c> when the
    /// scheduler paused a trigger itself — or <see langword="null" /> when nobody said. Blank reads as
    /// <see langword="null" />.
    /// </summary>
    public string? RequestedBy { get; init; }

    /// <summary>
    /// The pause as a store records it: both texts cut to their limits, and the instant it was made.
    /// </summary>
    internal PauseInfo Stamp(DateTimeOffset pausedAtUtc)
    {
        return new PauseInfo(Truncate(Reason, MaxReasonLength), Truncate(RequestedBy, MaxRequestedByLength), pausedAtUtc);
    }

    /// <summary>
    /// Cuts <paramref name="value" /> to <paramref name="maxLength" />, never between the two halves of a
    /// surrogate pair, and answers <see langword="null" /> for a blank one.
    /// </summary>
    internal static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        int length = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..length];
    }
}
