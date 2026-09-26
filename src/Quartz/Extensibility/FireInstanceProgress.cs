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

namespace Quartz.Extensibility;

/// <summary>
/// What a running firing last reported about how far it has got: the value
/// <see cref="IJobStore.UpdateFireInstanceProgress" /> is handed.
/// </summary>
/// <remarks>
/// <para>
/// A job reports it through <see cref="IJobExecutionContext.ReportProgress" />, and the scheduler hands
/// it to the store at most once a second per firing, and only when it has changed. A store keeps it
/// beside the firing and answers it on <see cref="FireInstance.Progress" /> and
/// <see cref="FireInstance.ProgressMessage" />.
/// </para>
/// <para>
/// A context object rather than two parameters, so that the next thing a report carries is a property
/// rather than a new overload.
/// </para>
/// </remarks>
public sealed class FireInstanceProgress
{
    /// <summary>
    /// The longest <see cref="Message" /> the scheduler hands on, in UTF-16 code units. A longer one is
    /// cut to this, which is what the ADO.NET store's <c>PROGRESS_MESSAGE</c> column holds.
    /// </summary>
    public const int MaxMessageLength = 250;

    /// <summary>
    /// How far the firing has got, from <c>0</c> to <c>100</c>.
    /// </summary>
    public required int Percent { get; init; }

    /// <summary>
    /// What the job said beside the percentage, or <see langword="null" /> when it said nothing.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Cuts a message to <see cref="MaxMessageLength" />, never between the two halves of a surrogate
    /// pair.
    /// </summary>
    internal static string? Truncate(string? message)
    {
        if (message is null || message.Length <= MaxMessageLength)
        {
            return message;
        }

        int length = char.IsHighSurrogate(message[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength;
        return message[..length];
    }
}
