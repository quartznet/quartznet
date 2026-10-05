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
using System.Text;

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Where every mark of the run chart goes, worked out once from an <see cref="ExecutionStatistics" /> so the
/// component only writes it out.
/// </summary>
/// <remarks>
/// <para>
/// Two panels over one time axis: runs per bucket stacked by result, and beneath it the median and 95th
/// percentile durations on an axis of their own. Counts and durations share no scale, so they are never drawn
/// against one.
/// </para>
/// <para>
/// The axis runs from the window's start to its end one slot per bucket size, so a stretch with no run is a
/// gap rather than a squeezed-out column. Past <see cref="MaxSlots" /> slots only the buckets that hold a run
/// are drawn, side by side.
/// </para>
/// </remarks>
internal sealed class RunStatisticsLayout
{
    /// <summary>Every panel's width, in the SVG's own units.</summary>
    public const double Width = 960;

    /// <summary>Where the plots start, leaving room for the value axis's labels.</summary>
    public const double PlotLeft = 64;

    /// <summary>Where the plots end, leaving room for the last time label.</summary>
    public const double PlotRight = Width - 28;

    /// <summary>The runs panel's height, plot and margins.</summary>
    public const double RunsHeight = 168;

    /// <summary>The durations panel's height, plot, margins and the time axis beneath.</summary>
    public const double DurationsHeight = 140;

    /// <summary>Above each plot, for the top tick's label.</summary>
    public const double PlotTop = 18;

    /// <summary>The runs plot's height.</summary>
    public const double RunsPlotHeight = 140;

    /// <summary>The durations plot's height.</summary>
    public const double DurationsPlotHeight = 92;

    /// <summary>The most slots drawn one per bucket size; past it, only the buckets that hold a run.</summary>
    public const int MaxSlots = 400;

    /// <summary>The thickest a column is drawn, however wide its slot.</summary>
    private const double MaxBarWidth = 24;

    /// <summary>The surface showing between two results of one column.</summary>
    private const double SegmentGap = 2;

    /// <summary>The shortest a result that happened is drawn, so one failure among thousands still shows.</summary>
    private const double MinSegmentHeight = 2;

    /// <summary>The rounding of a column's data end.</summary>
    private const double CornerRadius = 4;

    /// <summary>The steps an axis's top is rounded up to, times a power of ten.</summary>
    private static readonly long[] NiceMultiples = [1, 2, 4, 6, 8, 10];

    /// <summary>The results in the order a column stacks them, from the baseline up.</summary>
    public static readonly JobRunResult[] StackOrder = [JobRunResult.Failed, JobRunResult.Cancelled, JobRunResult.Skipped, JobRunResult.Succeeded];

    private RunStatisticsLayout()
    {
    }

    /// <summary>The columns, one per bucket that holds a run, oldest first.</summary>
    public List<Column> Columns { get; } = [];

    /// <summary>Every slot of the axis, one per bucket size whether or not it holds a run.</summary>
    public int SlotCount { get; private set; }

    /// <summary>How wide one slot is.</summary>
    public double SlotWidth { get; private set; }

    /// <summary>The labelled values of the runs axis, from zero up.</summary>
    public List<Tick> RunTicks { get; } = [];

    /// <summary>The labelled values of the durations axis, from zero up.</summary>
    public List<Tick> DurationTicks { get; } = [];

    /// <summary>The labelled instants of the time axis.</summary>
    public List<TimeTick> TimeTicks { get; } = [];

    /// <summary>The median line, one subpath per run of consecutive buckets.</summary>
    public string P50Path { get; private set; } = string.Empty;

    /// <summary>The 95th percentile line, one subpath per run of consecutive buckets.</summary>
    public string P95Path { get; private set; } = string.Empty;

    /// <summary>The median's points with no neighbour to join, drawn as dots.</summary>
    public List<Point> P50Dots { get; } = [];

    /// <summary>The 95th percentile's points with no neighbour to join, drawn as dots.</summary>
    public List<Point> P95Dots { get; } = [];

    /// <summary>The largest column's run count.</summary>
    public long MostRuns { get; private set; }

    /// <summary>The highest 95th percentile.</summary>
    public TimeSpan HighestP95 { get; private set; }

    /// <summary>
    /// Lays a chart out.
    /// </summary>
    /// <param name="statistics">The buckets, oldest first.</param>
    /// <param name="from">
    /// Where the window starts, or <see langword="null" /> to start at the oldest bucket.
    /// </param>
    /// <param name="before">Where the window ends, exclusive: usually now.</param>
    public static RunStatisticsLayout Create(ExecutionStatistics statistics, DateTimeOffset? from, DateTimeOffset before)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        RunStatisticsLayout layout = new();
        List<ExecutionStatisticsBucket> buckets = statistics.Buckets;
        if (buckets.Count == 0)
        {
            return layout;
        }

        long size = Math.Max(statistics.BucketSize.Ticks, 1);
        long firstIndex = from is { } start ? start.UtcTicks / size : buckets[0].StartUtc.UtcTicks / size;
        long lastIndex = (Math.Max(before.UtcTicks, 1) - 1) / size;
        firstIndex = Math.Min(firstIndex, buckets[0].StartUtc.UtcTicks / size);
        lastIndex = Math.Max(lastIndex, buckets[^1].StartUtc.UtcTicks / size);

        long slots = lastIndex - firstIndex + 1;
        bool dense = slots <= MaxSlots;
        layout.SlotCount = dense ? (int) slots : buckets.Count;
        layout.SlotWidth = (PlotRight - PlotLeft) / layout.SlotCount;

        layout.MostRuns = buckets.Max(static bucket => bucket.RunCount);
        layout.HighestP95 = buckets.Max(static bucket => bucket.P95Duration);

        long runsTop = NiceCeiling(layout.MostRuns);
        long durationTop = NiceCeiling((long) Math.Ceiling(layout.HighestP95.TotalMilliseconds));

        AddTicks(layout.RunTicks, runsTop, RunsPlotHeight, static value => value.ToString("N0", CultureInfo.InvariantCulture));
        AddTicks(layout.DurationTicks, durationTop, DurationsPlotHeight, static value => value == 0 ? "0" : DisplayValueHelper.FormatDuration(TimeSpan.FromMilliseconds(value)));

        double barWidth = Math.Min(MaxBarWidth, Math.Max(1, layout.SlotWidth * 0.62));

        for (int i = 0; i < buckets.Count; i++)
        {
            ExecutionStatisticsBucket bucket = buckets[i];
            int slot = dense ? (int) (bucket.StartUtc.UtcTicks / size - firstIndex) : i;
            double slotLeft = PlotLeft + slot * layout.SlotWidth;
            double centre = slotLeft + layout.SlotWidth / 2;

            layout.Columns.Add(new Column(
                bucket,
                slot,
                slotLeft,
                centre,
                Segments(bucket, centre - barWidth / 2, barWidth, runsTop),
                DurationY(bucket.P50Duration, durationTop),
                DurationY(bucket.P95Duration, durationTop)));
        }

        layout.P50Path = Line(layout.Columns, static column => column.P50Y, layout.P50Dots);
        layout.P95Path = Line(layout.Columns, static column => column.P95Y, layout.P95Dots);

        int step = Math.Max(1, (int) Math.Ceiling(layout.SlotCount / 6.0));
        for (int slot = 0; slot < layout.SlotCount; slot += step)
        {
            DateTimeOffset at = dense
                ? new DateTimeOffset((firstIndex + slot) * size, TimeSpan.Zero)
                : buckets[slot].StartUtc;

            layout.TimeTicks.Add(new TimeTick(PlotLeft + slot * layout.SlotWidth + layout.SlotWidth / 2, at));
        }

        return layout;
    }

    /// <summary>
    /// The smallest of 1, 2, 4, 6, 8 and 10 times a power of ten at or above <paramref name="value" />, so
    /// the axis's middle tick is a whole number too.
    /// </summary>
    internal static long NiceCeiling(long value)
    {
        if (value <= 1)
        {
            return 1;
        }

        long magnitude = 1;
        while (magnitude * 10 <= value)
        {
            magnitude *= 10;
        }

        foreach (long multiple in NiceMultiples)
        {
            if (multiple * magnitude >= value)
            {
                return multiple * magnitude;
            }
        }

        return 10 * magnitude;
    }

    /// <summary>Formats a coordinate the way an SVG attribute takes it.</summary>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void AddTicks(List<Tick> ticks, long top, double plotHeight, Func<long, string> label)
    {
        double baseline = PlotTop + plotHeight;
        ticks.Add(new Tick(baseline, label(0)));
        if (top % 2 == 0)
        {
            ticks.Add(new Tick(baseline - plotHeight / 2, label(top / 2)));
        }

        ticks.Add(new Tick(PlotTop, label(top)));
    }

    private static double DurationY(TimeSpan duration, long topMilliseconds)
    {
        double fraction = Math.Min(1, duration.TotalMilliseconds / topMilliseconds);
        return PlotTop + DurationsPlotHeight - fraction * DurationsPlotHeight;
    }

    /// <summary>
    /// A column's results from the baseline up, each at least <see cref="MinSegmentHeight" /> tall, with the
    /// surface showing between two of them and the top one's data end rounded.
    /// </summary>
    private static List<Segment> Segments(ExecutionStatisticsBucket bucket, double left, double width, long runsTop)
    {
        List<Segment> segments = [];
        double baseline = PlotTop + RunsPlotHeight;
        double bottom = baseline;

        foreach (JobRunResult result in StackOrder)
        {
            long count = bucket.CountOf(result);
            if (count == 0)
            {
                continue;
            }

            double height = Math.Max(MinSegmentHeight, count / (double) runsTop * RunsPlotHeight);
            segments.Add(new Segment(result, count, bottom - height, bottom));
            bottom -= height;
        }

        List<Segment> drawn = new(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i];

            // The gap is taken from the segment above it, never from the baseline.
            double lower = i > 0 && segment.Bottom - segment.Top > SegmentGap + 1 ? segment.Bottom - SegmentGap : segment.Bottom;
            double radius = i == segments.Count - 1 ? Math.Min(CornerRadius, Math.Min(width / 2, lower - segment.Top)) : 0;

            drawn.Add(segment with { Bottom = lower, Path = RoundedTop(left, segment.Top, width, lower, radius) });
        }

        return drawn;
    }

    private static string RoundedTop(double left, double top, double width, double bottom, double radius)
    {
        double right = left + width;
        if (radius <= 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"M{Number(left)},{Number(bottom)}V{Number(top)}H{Number(right)}V{Number(bottom)}Z");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"M{Number(left)},{Number(bottom)}V{Number(top + radius)}Q{Number(left)},{Number(top)} {Number(left + radius)},{Number(top)}H{Number(right - radius)}Q{Number(right)},{Number(top)} {Number(right)},{Number(top + radius)}V{Number(bottom)}Z");
    }

    /// <summary>
    /// A line through the columns' points, broken wherever a slot has no column: no run, no duration.
    /// </summary>
    private static string Line(List<Column> columns, Func<Column, double> y, List<Point> dots)
    {
        StringBuilder path = new();
        int runStart = 0;

        for (int i = 0; i <= columns.Count; i++)
        {
            bool broken = i == columns.Count || (i > 0 && columns[i].Slot != columns[i - 1].Slot + 1);
            if (!broken)
            {
                continue;
            }

            if (i - runStart == 1)
            {
                dots.Add(new Point(columns[runStart].Centre, y(columns[runStart])));
            }
            else
            {
                for (int j = runStart; j < i; j++)
                {
                    path.Append(j == runStart ? 'M' : 'L').Append(Number(columns[j].Centre)).Append(',').Append(Number(y(columns[j])));
                }
            }

            runStart = i;
        }

        return path.ToString();
    }

    /// <summary>One bucket's column and its points on the duration lines.</summary>
    internal sealed record Column(
        ExecutionStatisticsBucket Bucket,
        int Slot,
        double SlotLeft,
        double Centre,
        List<Segment> Segments,
        double P50Y,
        double P95Y);

    /// <summary>One result's share of a column.</summary>
    internal sealed record Segment(JobRunResult Result, long Count, double Top, double Bottom)
    {
        public string Path { get; init; } = string.Empty;
    }

    /// <summary>A labelled value on a panel's axis.</summary>
    internal sealed record Tick(double Y, string Label);

    /// <summary>A labelled instant on the time axis, at its slot's centre.</summary>
    internal sealed record TimeTick(double X, DateTimeOffset At);

    /// <summary>A point a line has no neighbour to join to.</summary>
    internal sealed record Point(double X, double Y);
}
