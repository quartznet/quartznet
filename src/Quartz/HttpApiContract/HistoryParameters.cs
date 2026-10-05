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

namespace Quartz.HttpApiContract;

/// <summary>
/// What a history listing asked for beyond its page and its 4.1 filters: one job, a fire-time window, a
/// set of results and a set of misfire reasons, as the query string carried them.
/// </summary>
/// <remarks>
/// <para>
/// A set is sent either as the parameter repeated or as one comma-separated value, and both spellings read
/// the same. Each member is a name, matched case-insensitively, or the number of a defined member, as every
/// enum on the wire is read.
/// </para>
/// <para>
/// Read where an operation applies them, after the scheduler is looked up, as the matchers of
/// <see cref="ListingParameters" /> are.
/// </para>
/// </remarks>
internal sealed record HistoryParameters
{
    /// <summary>
    /// The misfire reasons a client from before 4.4 can read, and what the misfire listing answers with
    /// when a request names none.
    /// </summary>
    /// <remarks>
    /// A 4.3 client reads <see cref="MisfireReason" /> through an enum converter with no
    /// <see cref="MisfireReason.Vetoed" />, and one such row fails its whole listing. A client that can read
    /// a reason names it. The set is frozen: a reason added later is also one an older client cannot read.
    /// </remarks>
    public static readonly IReadOnlyCollection<MisfireReason> ReasonsEveryReaderKnows = [MisfireReason.Missed, MisfireReason.Overlap];

    public string? JobGroup { get; init; }

    public string? JobName { get; init; }

    public DateTimeOffset? FiredFrom { get; init; }

    public DateTimeOffset? FiredBefore { get; init; }

    /// <summary>
    /// The <c>results</c> parameter's values: <see cref="JobRunResult" /> names.
    /// </summary>
    public string[]? Results { get; init; }

    /// <summary>
    /// The <c>reasons</c> parameter's values: <see cref="MisfireReason" /> names.
    /// </summary>
    public string[]? Reasons { get; init; }

    /// <summary>
    /// The job both halves name, or <see langword="null" /> when neither is given.
    /// </summary>
    /// <exception cref="InvalidRequestException">Only one half is given.</exception>
    public JobKey? Job()
    {
        bool hasGroup = !string.IsNullOrWhiteSpace(JobGroup);
        bool hasName = !string.IsNullOrWhiteSpace(JobName);
        if (hasGroup != hasName)
        {
            throw new InvalidRequestException("Both jobName and jobGroup must be given to filter by job");
        }

        return hasName ? new JobKey(JobName!, JobGroup!) : null;
    }

    /// <summary>
    /// The job both halves name, or the group alone: the run statistics count a whole group, where a listing
    /// takes both halves or neither.
    /// </summary>
    /// <exception cref="InvalidRequestException">A name without its group.</exception>
    public (JobKey? Job, string? Group) JobOrGroup()
    {
        bool hasGroup = !string.IsNullOrWhiteSpace(JobGroup);
        bool hasName = !string.IsNullOrWhiteSpace(JobName);
        if (hasName && !hasGroup)
        {
            throw new InvalidRequestException("jobName needs jobGroup: a job is named by both halves of its key");
        }

        return hasName ? (new JobKey(JobName!, JobGroup!), null) : (null, hasGroup ? JobGroup : null);
    }

    /// <summary>
    /// The results asked for, or <see langword="null" /> for every result.
    /// </summary>
    /// <exception cref="InvalidRequestException">A value names no result.</exception>
    public IReadOnlyCollection<JobRunResult>? ResultSet() => ReadSet<JobRunResult>(Results, "results");

    /// <summary>
    /// The misfire reasons asked for, or <see cref="ReasonsEveryReaderKnows" /> when the request named none.
    /// </summary>
    /// <exception cref="InvalidRequestException">A value names no reason.</exception>
    public IReadOnlyCollection<MisfireReason> ReasonSet() => ReadSet<MisfireReason>(Reasons, "reasons") ?? ReasonsEveryReaderKnows;

    private static List<TEnum>? ReadSet<TEnum>(string[]? values, string parameter) where TEnum : struct, Enum
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        List<TEnum> set = [];
        foreach (string? value in values)
        {
            if (value is null)
            {
                continue;
            }

            // Split before parsing: Enum.TryParse reads "Failed,Skipped" as the two ORed together.
            foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(token, ignoreCase: true, out TEnum member) || !Enum.IsDefined(member))
                {
                    throw new InvalidRequestException(
                        $"Unknown {parameter} value '{token}'. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}");
                }

                if (!set.Contains(member))
                {
                    set.Add(member);
                }
            }
        }

        return set.Count == 0 ? null : set;
    }
}
