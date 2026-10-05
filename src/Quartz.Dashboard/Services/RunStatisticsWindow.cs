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

namespace Quartz.Dashboard.Services;

/// <summary>
/// A stretch of fire time the History and Job Detail pages chart and list, and the bucket size it is
/// charted in.
/// </summary>
/// <remarks>
/// A window starts on a bucket boundary, so its first bucket is whole: <em>Last 24 hours</em> at 14:23 runs
/// from 14:00 the day before. <see cref="All" /> has no start; the history's bounds are its start.
/// </remarks>
/// <param name="Key">The <c>window</c> query parameter's value, and the dropdown's.</param>
/// <param name="Label">What the dropdown says.</param>
/// <param name="Scope">What a figure over the window is said to cover, as a stat card's title says it.</param>
/// <param name="Span">How far back it reaches, or <see langword="null" /> for the whole history.</param>
/// <param name="BucketSize">How much fire time one bucket covers.</param>
internal sealed record RunStatisticsWindow(string Key, string Label, string Scope, TimeSpan? Span, TimeSpan BucketSize)
{
    /// <summary>
    /// Everything the history keeps. Charted by the day, or by the hour when every run fired within
    /// <see cref="HourlyReach" /> of now.
    /// </summary>
    public static readonly RunStatisticsWindow All = new(string.Empty, "All retained", "all retained", null, TimeSpan.FromDays(1));

    public static readonly RunStatisticsWindow LastHour = new("1h", "Last hour", "last hour", TimeSpan.FromHours(1), TimeSpan.FromMinutes(5));

    public static readonly RunStatisticsWindow LastDay = new("24h", "Last 24 hours", "last 24 hours", TimeSpan.FromHours(24), TimeSpan.FromHours(1));

    public static readonly RunStatisticsWindow LastWeek = new("7d", "Last 7 days", "last 7 days", TimeSpan.FromDays(7), TimeSpan.FromHours(6));

    public static readonly RunStatisticsWindow LastMonth = new("30d", "Last 30 days", "last 30 days", TimeSpan.FromDays(30), TimeSpan.FromDays(1));

    /// <summary>The windows the pages offer, in the order they offer them.</summary>
    public static readonly RunStatisticsWindow[] Offered = [All, LastHour, LastDay, LastWeek, LastMonth];

    /// <summary>
    /// How recent every run of <see cref="All" /> must be for it to be charted by the hour: three days, at
    /// most 72 columns.
    /// </summary>
    public static readonly TimeSpan HourlyReach = TimeSpan.FromDays(3);

    /// <summary>The window a <c>window</c> query parameter names, or <see cref="All" /> for one nobody knows.</summary>
    public static RunStatisticsWindow Parse(string? key)
    {
        string trimmed = key?.Trim() ?? string.Empty;
        foreach (RunStatisticsWindow window in Offered)
        {
            if (string.Equals(window.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return All;
    }

    /// <summary>
    /// Where the window starts at <paramref name="now" />: on the bucket boundary at or before
    /// <c>now - Span</c>, or <see langword="null" /> for <see cref="All" />.
    /// </summary>
    public DateTimeOffset? From(DateTimeOffset now)
    {
        return Span is { } span ? AlignDown(now - span, BucketSize) : null;
    }

    /// <summary>The bucket boundary at or before <paramref name="instant" />.</summary>
    public static DateTimeOffset AlignDown(DateTimeOffset instant, TimeSpan bucketSize)
    {
        long ticks = instant.UtcTicks;
        return new DateTimeOffset(ticks - ticks % bucketSize.Ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// Reads a window's statistics: what the chart draws, and the window it draws them over.
    /// </summary>
    /// <param name="api">The data source.</param>
    /// <param name="filters">The page's filters; the window and the bucket size are set here.</param>
    /// <param name="now">The end of the window.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="NotSupportedException">The data source cannot count runs.</exception>
    public async Task<RunStatisticsView> Read(
        IQuartzApiClient api,
        ExecutionStatisticsQuery filters,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(filters);

        if (From(now) is { } from)
        {
            ExecutionStatistics windowed = await api.QueryExecutionStatistics(
                filters with { FiredFrom = from, BucketSize = BucketSize }, cancellationToken).ConfigureAwait(false);

            return new RunStatisticsView(this, windowed, from, now);
        }

        ExecutionStatistics daily = await api.QueryExecutionStatistics(
            filters with { FiredFrom = null, BucketSize = BucketSize }, cancellationToken).ConfigureAwait(false);

        if (daily.Buckets.Count == 0 || daily.Buckets[0].StartUtc < AlignDown(now, BucketSize) - HourlyReach + BucketSize)
        {
            return new RunStatisticsView(this, daily, null, now);
        }

        ExecutionStatistics hourly = await api.QueryExecutionStatistics(
            filters with { FiredFrom = null, BucketSize = TimeSpan.FromHours(1) }, cancellationToken).ConfigureAwait(false);

        return new RunStatisticsView(this, hourly, null, now);
    }
}

/// <summary>
/// A window's statistics as read: the buckets, and the stretch of time the chart lays them over.
/// </summary>
/// <param name="Window">The window asked for.</param>
/// <param name="Statistics">The buckets.</param>
/// <param name="From">Where the axis starts, or <see langword="null" /> to start at the oldest bucket.</param>
/// <param name="Before">Where the axis ends: the moment it was read.</param>
internal sealed record RunStatisticsView(RunStatisticsWindow Window, ExecutionStatistics Statistics, DateTimeOffset? From, DateTimeOffset Before);
