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

#nullable enable

using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// A running firing's log read from a cursor (#4437): the entry sequence number, which survives the capture
/// dropping its oldest entries and says how many of them the reader missed.
/// </summary>
public sealed class ExecutionLogBufferReadTest
{
    private static readonly DateTimeOffset now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void FromZeroEveryKeptEntryIsRead()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 10);
        Append(buffer, 1, 3);

        ExecutionLogPage page = buffer.Read(after: 0, take: 500, FireInstanceLogs.MaxAnswerBytes);

        Messages(page).Should().Equal(["line 1", "line 2", "line 3"]);
        page.Entries[0].Should().Be("2030-01-01T12:00:00.000Z info Job: line 1", "an entry reads as Execution Detail prints it");
        page.Next.Should().Be(3);
        page.Missed.Should().Be(0);
        page.HasMore.Should().BeFalse();
    }

    [Test]
    public void FromACursorTheRestIsReadAndTheCursorGrows()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 10);
        Append(buffer, 1, 3);

        ExecutionLogPage rest = buffer.Read(after: 2, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(rest).Should().Equal(["line 3"]);
        rest.Next.Should().Be(3);

        ExecutionLogPage nothing = buffer.Read(after: 3, take: 500, FireInstanceLogs.MaxAnswerBytes);
        nothing.Entries.Should().BeEmpty("the reader has every entry there is");
        nothing.Next.Should().Be(3, "the cursor stays where the reader is");

        Append(buffer, 4, 5);
        ExecutionLogPage more = buffer.Read(after: 3, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(more).Should().Equal(["line 4", "line 5"]);
        more.Next.Should().Be(5);
    }

    /// <summary>
    /// The design guard of the cursor: missed is the entries dropped after the cursor, not the total dropped, which
    /// includes the ones the reader already had.
    /// </summary>
    [Test]
    public void MissedCountsOnlyTheEntriesDroppedAfterTheCursor()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 3);
        Append(buffer, 1, 5);

        ExecutionLogPage first = buffer.Read(after: 0, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(first).Should().Equal(["line 3", "line 4", "line 5"]);
        first.Missed.Should().Be(2, "lines 1 and 2 were dropped before the reader got them");
        first.Next.Should().Be(5);

        Append(buffer, 6, 8);
        ExecutionLogPage second = buffer.Read(after: 5, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(second).Should().Equal(["line 6", "line 7", "line 8"]);
        second.Missed.Should().Be(0,
            "five entries have been dropped in all, and the reader had every one of them: none was dropped after its cursor");

        Append(buffer, 9, 13);
        ExecutionLogPage third = buffer.Read(after: 8, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(third).Should().Equal(["line 11", "line 12", "line 13"]);
        third.Missed.Should().Be(2, "lines 9 and 10 came and went between two reads");
        third.Next.Should().Be(13);
        buffer.Dropped.Should().Be(10, "the total the run's log opens with is unchanged by reading");
    }

    [Test]
    public void TakeBoundsAReadAndSaysThereIsMore()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 10);
        Append(buffer, 1, 5);

        ExecutionLogPage page = buffer.Read(after: 0, take: 2, FireInstanceLogs.MaxAnswerBytes);

        Messages(page).Should().Equal(["line 1", "line 2"]);
        page.Next.Should().Be(2);
        page.HasMore.Should().BeTrue("three more entries are kept");

        ExecutionLogPage rest = buffer.Read(page.Next, take: 500, FireInstanceLogs.MaxAnswerBytes);
        Messages(rest).Should().Equal(["line 3", "line 4", "line 5"]);
        rest.HasMore.Should().BeFalse();
    }

    [Test]
    public void AReadStopsBeforeTheEntryThatWouldTakeItPastTheByteCap()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 100, maxBytes: 1024 * 1024);
        for (int i = 1; i <= 10; i++)
        {
            buffer.Append(LogLevel.Information, "Job", i.ToString(CultureInfo.InvariantCulture) + new string('x', 10_000), exception: null);
        }

        ExecutionLogPage page = buffer.Read(after: 0, take: 500, FireInstanceLogs.MaxAnswerBytes);

        page.Entries.Should().HaveCount(6, "six entries of about 10 KB fit in 64 KB and a seventh does not");
        page.Entries.Sum(entry => Encoding.UTF8.GetByteCount(entry) + 1).Should().BeLessThanOrEqualTo(FireInstanceLogs.MaxAnswerBytes);
        page.HasMore.Should().BeTrue();
        page.Next.Should().Be(6);
    }

    [Test]
    public void AnEntryLargerThanTheByteCapStillComesBackAlone()
    {
        ExecutionLogBuffer buffer = Buffer(maxLines: 10, maxBytes: 1024 * 1024);
        buffer.Append(LogLevel.Information, "Job", new string('x', 100_000), exception: null);
        buffer.Append(LogLevel.Information, "Job", "line 2", exception: null);

        ExecutionLogPage page = buffer.Read(after: 0, take: 500, FireInstanceLogs.MaxAnswerBytes);

        page.Entries.Should().ContainSingle("an application may raise MaxBytes past the cap, and the reader must still move on")
            .Which.Length.Should().BeGreaterThan(FireInstanceLogs.MaxAnswerBytes);
        page.Next.Should().Be(1);
        page.HasMore.Should().BeTrue();

        Messages(buffer.Read(page.Next, take: 500, FireInstanceLogs.MaxAnswerBytes)).Should().Equal(["line 2"]);
    }

    [Test]
    public void AReaderFollowingAnotherThreadsAppendsSeesEveryEntryOrIsToldItMissedIt()
    {
        const int total = 20_000;
        ExecutionLogBuffer buffer = Buffer(maxLines: 50, maxBytes: 1024 * 1024);

        Task writer = Task.Run(() => Append(buffer, 1, total));

        List<int> seen = [];
        long missed = 0;
        long cursor = 0;
        while (cursor < total)
        {
            ExecutionLogPage page = buffer.Read(cursor, take: 7, FireInstanceLogs.MaxAnswerBytes);
            page.Next.Should().BeGreaterThanOrEqualTo(cursor, "a cursor never goes back");
            missed += page.Missed;
            seen.AddRange(Messages(page).Select(message => int.Parse(message["line ".Length..], CultureInfo.InvariantCulture)));
            cursor = page.Next;
        }

        writer.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();

        seen.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems("each entry is read once, in the order it was logged");
        (seen.Count + missed).Should().Be(total, "an entry the reader did not get is one it was told it missed");
    }

    private static ExecutionLogBuffer Buffer(int maxLines, int maxBytes = 64 * 1024)
    {
        return new ExecutionLogBuffer(maxLines, maxBytes, new FakeTimeProvider(now));
    }

    private static void Append(ExecutionLogBuffer buffer, int from, int to)
    {
        for (int i = from; i <= to; i++)
        {
            buffer.Append(LogLevel.Information, "Job", "line " + i.ToString(CultureInfo.InvariantCulture), exception: null);
        }
    }

    private static string[] Messages(ExecutionLogPage page)
    {
        return [.. page.Entries.Select(entry => entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..])];
    }
}
