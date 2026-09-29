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

namespace Quartz.Tests.Unit;

/// <summary>
/// What a job's <see cref="JobRunReport" /> says, as each factory and <see cref="JobRunReport.With" />
/// build it.
/// </summary>
public sealed class JobRunReportTest
{
    [Test]
    public void EachFactorySaysItsOwnResult()
    {
        JobRunReport.Succeeded().Result.Should().Be(JobRunResult.Succeeded);
        JobRunReport.Skipped().Result.Should().Be(JobRunResult.Skipped);
        JobRunReport.Failed().Result.Should().Be(JobRunResult.Failed);

        JobRunReport.Skipped("nothing to do").Summary.Should().Be("nothing to do");
        JobRunReport.Succeeded().Summary.Should().BeNull("a summary is the job's to give, and it gave none");
        JobRunReport.Failed().Metrics.Should().BeNull("no metric was added");
    }

    [Test]
    public void ANewReportIsASuccess()
    {
        new JobRunReport().Result.Should().Be(JobRunResult.Succeeded,
            "Succeeded is the enum's zero, so an object initialiser that names only a summary reports a success");
    }

    [Test]
    public void WithAddsAMetricToACopy()
    {
        JobRunReport original = JobRunReport.Skipped("no stale reservations");

        JobRunReport withOne = original.With("released", 0);
        JobRunReport withTwo = withOne.With("scanned", 1200L);

        original.Metrics.Should().BeNull("a report is a value, and With leaves the one it was called on alone");
        withOne.Metrics.Should().BeEquivalentTo(new Dictionary<string, object?> { ["released"] = 0 });
        withTwo.Metrics.Should().BeEquivalentTo(new Dictionary<string, object?> { ["released"] = 0, ["scanned"] = 1200L });

        withTwo.Result.Should().Be(JobRunResult.Skipped, "the copy keeps everything but the metrics");
        withTwo.Summary.Should().Be("no stale reservations");
    }

    [Test]
    public void TheLastValueForANameWins()
    {
        JobRunReport report = JobRunReport.Succeeded().With("rows", 1).With("rows", 2);

        report.Metrics.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("rows", 2),
            "a metric measured twice is reported with its latest value, not twice");
    }

    [Test]
    public void AMetricMayBeNull()
    {
        JobRunReport.Succeeded().With("lastError", null).Metrics.Should().ContainKey("lastError")
            .WhoseValue.Should().BeNull();
    }

    [Test]
    public void AMetricNeedsAName()
    {
        Action act = () => JobRunReport.Succeeded().With(null!, 1);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ItIsTheShippedReport()
    {
        IJobRunReport report = JobRunReport.Failed("the upstream refused").With("attempts", 3);

        report.Result.Should().Be(JobRunResult.Failed);
        report.Summary.Should().Be("the upstream refused");
        report.Metrics.Should().ContainKey("attempts");
    }
}
