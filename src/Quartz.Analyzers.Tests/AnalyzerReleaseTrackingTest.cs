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

namespace Quartz.Analyzers.Tests;

/// <summary>
/// The release-tracking files the RS2xxx analyzers read, held to what has actually shipped.
/// </summary>
/// <remarks>
/// RS2000 and its siblings already fail the build for a descriptor missing from both files, or listed
/// with a severity it does not have. What they cannot know is which Quartz release a rule shipped in,
/// and a rule still listed as unshipped after it shipped is one whose later change nobody is told
/// about. A release's block is history, so what it lists stays true after every later release.
/// </remarks>
public class AnalyzerReleaseTrackingTest
{
    [Test]
    public void RulesThatShippedIn420AreRecordedAsShippedIn420()
    {
        string[] shipped = File.ReadAllLines(Path.Combine(AnalyzerProject().FullName, "AnalyzerReleases.Shipped.md"));

        int start = Array.IndexOf(shipped, "## Release 4.2.0");
        start.Should().BeGreaterThanOrEqualTo(0, "4.2.0 is the first release that carries the analyzer, so it is the first block Shipped.md has");

        List<string> rules = shipped
            .Skip(start + 1)
            .TakeWhile(x => !x.StartsWith("## ", StringComparison.Ordinal))
            .Where(x => x.StartsWith("QZ", StringComparison.Ordinal))
            .Select(x => string.Join(" | ", x.Split('|').Take(3).Select(cell => cell.Trim())))
            .ToList();

        rules.Should().Equal(
            [
                "QZ0001 | Quartz | Error",
                "QZ0002 | Quartz | Error",
                "QZ0003 | Quartz | Warning",
                "QZ0004 | Quartz | Info",
                "QZ1001 | Quartz | Error",
                "QZ1002 | Quartz | Error",
                "QZ1003 | Quartz | Error",
                "QZ1004 | Quartz | Warning",
            ],
            "these are the rules 4.2.0 shipped, with the severities it shipped them at");
    }

    /// <summary>
    /// Walks up from the test assembly's directory to the one holding <c>Quartz.slnx</c>.
    /// </summary>
    private static DirectoryInfo AnalyzerProject()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Quartz.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tracking files are read from the source tree, so the test has to run inside one");

        return new DirectoryInfo(Path.Combine(directory!.FullName, "src", "Quartz.Analyzers"));
    }
}
