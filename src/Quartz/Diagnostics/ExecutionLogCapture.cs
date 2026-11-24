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
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.Extensions.Logging;

using Quartz.Core;

namespace Quartz.Diagnostics;

/// <summary>
/// Where the lines a firing logs are kept while it runs: one bounded buffer per firing, beside its
/// execution context.
/// </summary>
/// <remarks>
/// <para>
/// The buffer is begun by the capture middleware of a scheduler that asked for capture, and found again
/// by the capture logger through <see cref="AmbientJobExecution" />, which is how a line logged anywhere
/// on the firing's flow — the job, a service it called, work it started with <c>Task.Run</c> — reaches
/// that firing's buffer and no other. A line logged outside a firing, or inside a firing of a scheduler
/// that does not capture, finds no buffer and is not kept.
/// </para>
/// <para>
/// Kept in a <see cref="ConditionalWeakTable{TKey,TValue}" /> rather than on the context, for the reason
/// progress is: a field would grow every firing's context for the schedulers that capture. The buffer
/// goes with its context, so the history plugin can read it after the job has returned and nothing has
/// to remember to take it away.
/// </para>
/// <para>
/// Static rather than one per container, as <see cref="AmbientJobExecution" /> is: a flow is inside at
/// most one firing, however many containers the process holds.
/// </para>
/// </remarks>
internal static class ExecutionLogCapture
{
    private static readonly ConditionalWeakTable<IJobExecutionContext, ExecutionLogBuffer> buffers = new();

    /// <summary>
    /// Starts capturing for a firing, or does nothing when it already is — a refire runs the pipeline
    /// again with the same context, and its lines belong with the first attempt's.
    /// </summary>
    internal static void Begin(IJobExecutionContext context, ExecutionLogCaptureOptions options, TimeProvider timeProvider)
    {
        buffers.TryAdd(context, new ExecutionLogBuffer(options.MaxLines, options.MaxBytes, timeProvider));
    }

    /// <summary>
    /// The firing's buffer, or <see langword="null" /> when nothing captures for it.
    /// </summary>
    internal static ExecutionLogBuffer? Find(IJobExecutionContext context)
    {
        return buffers.TryGetValue(context, out ExecutionLogBuffer? buffer) ? buffer : null;
    }

    /// <summary>
    /// The buffer of the firing the current flow belongs to, or <see langword="null" /> when the flow
    /// belongs to none or its firing is not being captured.
    /// </summary>
    internal static ExecutionLogBuffer? Current
    {
        get
        {
            IJobExecutionContext? context = AmbientJobExecution.Current;
            return context is null ? null : Find(context);
        }
    }
}

/// <summary>
/// One firing's captured lines, oldest dropped first once either bound is reached.
/// </summary>
internal sealed class ExecutionLogBuffer
{
    private readonly Lock gate = new();
    private readonly Queue<(string Line, int Bytes)> lines = new();
    private readonly int maxLines;
    private readonly int maxBytes;
    private readonly TimeProvider timeProvider;

    private int bytes;
    private int dropped;

    /// <summary>
    /// How many entries were ever appended, kept or dropped: the sequence number the next one gets, and
    /// what a reader's cursor counts in (#4437).
    /// </summary>
    private long appended;

    internal ExecutionLogBuffer(int maxLines, int maxBytes, TimeProvider timeProvider)
    {
        this.maxLines = maxLines;
        this.maxBytes = maxBytes;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// How many entries were dropped to stay inside the bounds.
    /// </summary>
    internal int Dropped
    {
        get
        {
            lock (gate)
            {
                return dropped;
            }
        }
    }

    /// <summary>
    /// Keeps one log entry, dropping the oldest ones that no longer fit.
    /// </summary>
    internal void Append(LogLevel level, string category, string message, Exception? exception)
    {
        string line = Format(timeProvider.GetUtcNow(), level, category, message, exception);
        int size = Encoding.UTF8.GetByteCount(line) + 1;

        if (size > maxBytes)
        {
            line = Cut(line, maxBytes);
            size = Encoding.UTF8.GetByteCount(line) + 1;
        }

        lock (gate)
        {
            lines.Enqueue((line, size));
            bytes += size;
            appended++;

            while (lines.Count > maxLines || bytes > maxBytes)
            {
                (_, int oldest) = lines.Dequeue();
                bytes -= oldest;
                dropped++;
            }
        }
    }

    /// <summary>
    /// What was kept, one entry per line and oldest first, opening with how many were dropped when any
    /// were; <see langword="null" /> when the firing logged nothing.
    /// </summary>
    internal string? ToText()
    {
        lock (gate)
        {
            if (lines.Count == 0 && dropped == 0)
            {
                return null;
            }

            StringBuilder text = new(bytes + 96);
            if (dropped > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"[{dropped} earlier log entries dropped: the capture keeps at most {maxLines} entries and {maxBytes} bytes]");
            }

            foreach ((string line, _) in lines)
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(line);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// The entries a reader that has <paramref name="after" /> of them does not have yet, oldest first: what a
    /// running firing's log is followed by (#4437).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cursor is the entry sequence number, the count of entries appended before the first one answered.
    /// The buffer drops its oldest entries, so the first one it still keeps is <c>appended - kept</c>: a cursor
    /// behind it resumes there, and the entries in between are counted as missed, not taken from the total
    /// dropped, which includes the ones the reader already had.
    /// </para>
    /// <para>
    /// At most <paramref name="take" /> entries, and none past <paramref name="maxBytes" /> of UTF-8 but the
    /// first: one entry larger than the cap still comes back alone, so a reader always moves on.
    /// </para>
    /// </remarks>
    /// <param name="after">How many entries the reader has: the <see cref="ExecutionLogPage.Next" /> of its last read, 0 at first.</param>
    /// <param name="take">The most entries to answer, at least 1.</param>
    /// <param name="maxBytes">The most UTF-8 bytes the entries answered may take, the first excepted.</param>
    internal ExecutionLogPage Read(long after, int take, int maxBytes)
    {
        lock (gate)
        {
            long first = appended - lines.Count;
            long start = Math.Clamp(after, first, appended);
            long missed = Math.Max(0, first - after);
            long skip = start - first;

            List<string> entries = [];
            long used = 0;
            long index = 0;
            foreach ((string line, int size) in lines)
            {
                if (index++ < skip)
                {
                    continue;
                }

                if (entries.Count >= take || (entries.Count > 0 && used + size > maxBytes))
                {
                    break;
                }

                entries.Add(line);
                used += size;
            }

            long next = start + entries.Count;
            return new ExecutionLogPage([.. entries], next, missed, next < appended);
        }
    }

    private static string Format(DateTimeOffset now, LogLevel level, string category, string message, Exception? exception)
    {
        string head = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                      + " " + LevelName(level) + " " + category + ": " + message;

        return exception is null ? head : head + "\n" + exception;
    }

    /// <summary>
    /// Four letters per level, as the console logger writes them, so a captured log reads like one.
    /// </summary>
    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        _ => "none",
    };

    /// <summary>
    /// Cuts an entry that is larger than the whole bound on its own down to a size that fits.
    /// </summary>
    /// <remarks>
    /// By characters, at a third of the byte budget: no character of UTF-16 takes more than three bytes
    /// of UTF-8 on its own, and a surrogate pair takes four for two, so what is kept always fits without
    /// the text being measured again character by character.
    /// </remarks>
    private static string Cut(string line, int byteBudget)
    {
        const string Marker = " [cut]";

        int keep = Math.Max(0, (byteBudget - 1 - Marker.Length) / 3);
        if (keep > 0 && char.IsHighSurrogate(line[keep - 1]))
        {
            keep--;
        }

        return string.Concat(line.AsSpan(0, keep), Marker);
    }
}

/// <summary>
/// One read of a firing's captured entries, from a cursor (#4437).
/// </summary>
/// <param name="Entries">The entries, oldest first, as <see cref="ExecutionLogBuffer.ToText" /> prints them.</param>
/// <param name="Next">The cursor of the next read: how many entries the reader has after this one.</param>
/// <param name="Missed">Entries the capture's bounds dropped after the cursor and before this read.</param>
/// <param name="HasMore">Whether more entries are kept than this read answered.</param>
internal sealed record ExecutionLogPage(string[] Entries, long Next, long Missed, bool HasMore);
