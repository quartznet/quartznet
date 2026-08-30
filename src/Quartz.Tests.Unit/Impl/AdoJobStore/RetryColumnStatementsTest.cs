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

using System.Text.RegularExpressions;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Where the retry columns sit in the trigger statements, and that every statement writing the
/// trigger row writes both of them.
/// </summary>
/// <remarks>
/// Parameters are bound in statement order, because a provider with positional binding takes them
/// that way — the rule #3512 was written for. A column added to the middle of a statement without the
/// binder moving with it is silent on SQL Server and wrong on Oracle, which is not a difference worth
/// finding on a dialect leg.
/// </remarks>
public class RetryColumnStatementsTest
{
    /// <summary>
    /// The four flavours of the trigger UPDATE. Each is picked by a different pair of decisions
    /// (dirty job data, changed pin), and all four write the whole trigger row.
    /// </summary>
    public static IEnumerable<TestCaseData> TriggerUpdates()
    {
        yield return new TestCaseData(StdAdoConstants.SqlUpdateTrigger).SetName("update");
        yield return new TestCaseData(StdAdoConstants.SqlUpdateTriggerWithPreferredNode).SetName("update, with the pin");
        yield return new TestCaseData(StdAdoConstants.SqlUpdateTriggerSkipData).SetName("update, skipping job data");
        yield return new TestCaseData(StdAdoConstants.SqlUpdateTriggerSkipDataWithPreferredNode).SetName("update, skipping job data, with the pin");
    }

    [TestCaseSource(nameof(TriggerUpdates))]
    public void EveryTriggerUpdateWritesBothRetryColumns(string sql)
    {
        sql.Should().Contain($"{AdoConstants.ColumnRetryPolicy} = @{SqlParameters.TriggerRetryPolicy}");
        sql.Should().Contain($"{AdoConstants.ColumnRetryAttempt} = @{SqlParameters.TriggerRetryAttempt}");
    }

    [TestCaseSource(nameof(TriggerUpdates))]
    public void TheRetryColumnsSitBetweenTheExecutionGroupAndThePin(string sql)
    {
        int executionGroup = sql.IndexOf(AdoConstants.ColumnExecutionGroup, StringComparison.Ordinal);
        int retryPolicy = sql.IndexOf(AdoConstants.ColumnRetryPolicy, StringComparison.Ordinal);
        int retryAttempt = sql.IndexOf(AdoConstants.ColumnRetryAttempt, StringComparison.Ordinal);

        retryPolicy.Should().BeGreaterThan(executionGroup);
        retryAttempt.Should().BeGreaterThan(retryPolicy);

        int preferredNode = sql.IndexOf(AdoConstants.ColumnPreferredNode, StringComparison.Ordinal);
        if (preferredNode >= 0)
        {
            retryAttempt.Should().BeLessThan(preferredNode,
                "the pin is written only when it changed, so it has to come last; the binder adds the retry parameters "
                + "before it and cannot know which flavour it is binding");
        }
    }

    /// <summary>
    /// The trigger INSERT names its columns and its placeholders in two lists, and nothing but this
    /// keeps them lined up.
    /// </summary>
    [Test]
    public void TheTriggerInsertLinesItsColumnsUpWithItsPlaceholders()
    {
        Match match = Regex.Match(
            StdAdoConstants.SqlInsertTrigger,
            @"INSERT INTO \S+ \((?<columns>[^)]*)\)\s*VALUES\((?<values>[^)]*)\)",
            RegexOptions.Singleline);

        match.Success.Should().BeTrue("the INSERT is a column list and a VALUES list");

        string[] columns = Split(match.Groups["columns"].Value);
        string[] placeholders = Split(match.Groups["values"].Value);

        placeholders.Length.Should().Be(columns.Length,
            "a column with no placeholder — or the other way round — is a statement that cannot execute");

        int policyIndex = Array.IndexOf(columns, AdoConstants.ColumnRetryPolicy);
        int attemptIndex = Array.IndexOf(columns, AdoConstants.ColumnRetryAttempt);

        policyIndex.Should().BeGreaterThanOrEqualTo(0);
        attemptIndex.Should().Be(policyIndex + 1, "the two retry columns are written together");

        placeholders[policyIndex].Should().Be("@" + SqlParameters.TriggerRetryPolicy);
        placeholders[attemptIndex].Should().Be("@" + SqlParameters.TriggerRetryAttempt);

        columns[^1].Should().Be(AdoConstants.ColumnRetryAttempt,
            "the retry columns are the newest, and appending is what keeps every other position unchanged");
    }

    /// <summary>
    /// The listing projection is read by ordinal, so the retry columns have to be where
    /// <c>ReadTriggerHeader</c> looks for them — ahead of the computed executing flag, which is the
    /// one column the reader takes from a fixed position at the end.
    /// </summary>
    [Test]
    public void TheTriggerListingProjectsTheRetryColumnsBeforeTheComputedFlag()
    {
        string sql = StdAdoConstants.SqlSelectTriggerHeaders;

        int executionGroup = sql.IndexOf(AdoConstants.ColumnExecutionGroup, StringComparison.Ordinal);
        int retryPolicy = sql.IndexOf(AdoConstants.ColumnRetryPolicy, StringComparison.Ordinal);
        int retryAttempt = sql.IndexOf(AdoConstants.ColumnRetryAttempt, StringComparison.Ordinal);
        int computedFlag = sql.IndexOf("CASE WHEN", StringComparison.Ordinal);

        retryPolicy.Should().BeGreaterThan(executionGroup);
        retryAttempt.Should().BeGreaterThan(retryPolicy);
        computedFlag.Should().BeGreaterThan(retryAttempt);
    }

    /// <summary>
    /// The single-trigger and batch reads share one column list, and <c>ReadMapFromReader(rs, 11)</c>
    /// takes the job data map from a fixed ordinal — so the retry columns belong at the end of it.
    /// </summary>
    [Test]
    public void TheTriggerSelectAppendsTheRetryColumnsAfterThePin()
    {
        string sql = StdAdoConstants.SqlSelectTrigger;

        int preferredNodeAuto = sql.IndexOf(AdoConstants.ColumnPreferredNodeAuto, StringComparison.Ordinal);
        int retryPolicy = sql.IndexOf(AdoConstants.ColumnRetryPolicy, StringComparison.Ordinal);
        int retryAttempt = sql.IndexOf(AdoConstants.ColumnRetryAttempt, StringComparison.Ordinal);

        preferredNodeAuto.Should().BeGreaterThanOrEqualTo(0);
        retryPolicy.Should().BeGreaterThan(preferredNodeAuto,
            "appending is what leaves every earlier ordinal — the job data map's above all — where the reader expects it");
        retryAttempt.Should().BeGreaterThan(retryPolicy);
    }

    private static string[] Split(string list)
    {
        return [.. list.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)];
    }
}
