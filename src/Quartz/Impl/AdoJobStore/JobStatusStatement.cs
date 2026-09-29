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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Which of the four <c>QRTZ_JOB_STATUS</c> updates one recorded execution folds in through.
/// </summary>
/// <remarks>
/// Chosen by the store from the execution rather than decided in SQL, because Firebird refuses a
/// parameter compared with a literal and every dialect spells a boolean differently. Four rather than
/// three: a failure the trigger retries moves the last failure, and a cancelled run does not, as
/// <see cref="JobRunStatusFold" /> has it.
/// </remarks>
internal enum JobStatusUpdate
{
    /// <summary><see cref="JobRunResult.Succeeded" /> or <see cref="JobRunResult.Skipped" />.</summary>
    Succeeded,

    /// <summary><see cref="JobRunResult.Failed" />, and the trigger did not answer with another attempt.</summary>
    FailedFinally,

    /// <summary><see cref="JobRunResult.Failed" />, and the trigger is trying again.</summary>
    FailedRetried,

    /// <summary><see cref="JobRunResult.Cancelled" />, or a result this version does not know.</summary>
    Other
}

/// <summary>
/// What a placeholder of a <c>QRTZ_JOB_STATUS</c> update is bound to.
/// </summary>
internal enum JobStatusValue
{
    SchedulerName,
    JobGroup,
    JobName,
    FiredTime,
    Result,
    RunTime,
    InstanceName,
    EntryId,
    Summary,
    FailureMessage
}

/// <summary>
/// One update of <c>QRTZ_JOB_STATUS</c>, the SQL and its placeholders in the order the SQL names them.
/// </summary>
/// <remarks>
/// <para>
/// The fold of <see cref="JobRunStatusFold" />, one row at a time in the database:
/// </para>
/// <list type="bullet">
/// <item><description>The counters are incremented in SQL, so two nodes folding into one row lose nothing.</description></item>
/// <item><description>
/// A <c>LAST_*</c> run column moves only when the execution fired at or after <c>LAST_FIRED_TIME</c>; one
/// that completes out of order is counted and leaves them alone.
/// </description></item>
/// <item><description>
/// <c>LAST_FIRED_TIME</c> is assigned last, and <c>LAST_FAILURE_MESSAGE</c> before <c>LAST_FAILURE_TIME</c>:
/// MySQL evaluates a <c>SET</c> list left to right, so a column assigned earlier reads its new value in
/// every expression after it.
/// </description></item>
/// </list>
/// <para>
/// The fire time is compared once per column, and each comparison is a placeholder of its own. A dialect
/// that binds by position needs one parameter per placeholder, so each is named for its value and its
/// position, and <see cref="Parameters" /> lists them in order for the binder to walk.
/// </para>
/// </remarks>
internal sealed class JobStatusStatement
{
    private JobStatusStatement(string sql, (string Name, JobStatusValue Value)[] parameters)
    {
        Sql = sql;
        Parameters = parameters;
    }

    /// <summary>The statement, with the table prefix still to substitute.</summary>
    public string Sql { get; }

    /// <summary>Every placeholder of <see cref="Sql" />, in the order it names them.</summary>
    public IReadOnlyList<(string Name, JobStatusValue Value)> Parameters { get; }

    /// <summary>Which update folds <paramref name="entry" /> in.</summary>
    internal static JobStatusUpdate For(ExecutionHistoryEntry entry)
    {
        return entry.EffectiveResult switch
        {
            JobRunResult.Succeeded or JobRunResult.Skipped => JobStatusUpdate.Succeeded,
            JobRunResult.Failed => entry.RetryScheduled ? JobStatusUpdate.FailedRetried : JobStatusUpdate.FailedFinally,
            _ => JobStatusUpdate.Other
        };
    }

    /// <summary>Builds the update for one kind of execution.</summary>
    internal static JobStatusStatement Update(JobStatusUpdate kind)
    {
        Builder sql = new();

        sql.Text("UPDATE ").Text(StdAdoConstants.TablePrefixSubst).Text(AdoConstants.TableJobStatus)
            .Text(" SET ").Text(AdoConstants.ColumnRunCount).Text(" = ").Text(AdoConstants.ColumnRunCount).Text(" + 1");

        if (kind == JobStatusUpdate.FailedFinally)
        {
            sql.Text(", ").Text(AdoConstants.ColumnFailureCount).Text(" = ").Text(AdoConstants.ColumnFailureCount).Text(" + 1");
        }

        // The earliest fire time is a minimum, whichever run is newest.
        sql.Text(", ").Text(AdoConstants.ColumnFirstFiredTime).Text(" = CASE WHEN ").Text(AdoConstants.ColumnFirstFiredTime)
            .Text(" > ").Parameter(JobStatusValue.FiredTime).Text(" THEN ").Parameter(JobStatusValue.FiredTime)
            .Text(" ELSE ").Text(AdoConstants.ColumnFirstFiredTime).Text(" END");

        if (kind == JobStatusUpdate.Succeeded)
        {
            sql.Newest(AdoConstants.ColumnConsecutiveFailures, static b => b.Text("0"));
        }
        else if (kind == JobStatusUpdate.FailedFinally)
        {
            sql.Newest(AdoConstants.ColumnConsecutiveFailures, static b => b.Text(AdoConstants.ColumnConsecutiveFailures).Text(" + 1"));
        }

        sql.Newest(AdoConstants.ColumnLastResult, static b => b.Parameter(JobStatusValue.Result));
        sql.Newest(AdoConstants.ColumnLastRunTime, static b => b.Parameter(JobStatusValue.RunTime));
        sql.Newest(AdoConstants.ColumnLastInstanceName, static b => b.Parameter(JobStatusValue.InstanceName));
        sql.Newest(AdoConstants.ColumnLastEntryId, static b => b.Parameter(JobStatusValue.EntryId));
        sql.Newest(AdoConstants.ColumnLastSummary, static b => b.Parameter(JobStatusValue.Summary));

        if (kind == JobStatusUpdate.Succeeded)
        {
            sql.Later(AdoConstants.ColumnLastSuccessTime, AdoConstants.ColumnLastSuccessTime, JobStatusValue.FiredTime);
        }
        else if (kind is JobStatusUpdate.FailedFinally or JobStatusUpdate.FailedRetried)
        {
            // The message first: it is compared against the failure time this statement then moves.
            sql.Later(AdoConstants.ColumnLastFailureMessage, AdoConstants.ColumnLastFailureTime, JobStatusValue.FailureMessage);
            sql.Later(AdoConstants.ColumnLastFailureTime, AdoConstants.ColumnLastFailureTime, JobStatusValue.FiredTime);
        }

        // Last, so that every comparison above read the value this execution found.
        sql.Newest(AdoConstants.ColumnLastFiredTime, static b => b.Parameter(JobStatusValue.FiredTime));

        sql.Text(" WHERE ").Text(AdoConstants.ColumnSchedulerName).Text(" = ").Parameter(JobStatusValue.SchedulerName)
            .Text(" AND ").Text(AdoConstants.ColumnJobGroup).Text(" = ").Parameter(JobStatusValue.JobGroup)
            .Text(" AND ").Text(AdoConstants.ColumnJobName).Text(" = ").Parameter(JobStatusValue.JobName);

        return sql.Build();
    }

    private sealed class Builder
    {
        private readonly StringBuilder text = new();
        private readonly List<(string Name, JobStatusValue Value)> parameters = [];

        public Builder Text(string value)
        {
            text.Append(value);
            return this;
        }

        public Builder Parameter(JobStatusValue value)
        {
            // Named for what it holds and where it stands, so every placeholder is a name of its own.
            string name = "status" + value + parameters.Count.ToString("00", CultureInfo.InvariantCulture);
            parameters.Add((name, value));
            text.Append('@').Append(name);
            return this;
        }

        /// <summary>
        /// <c>, column = CASE WHEN LAST_FIRED_TIME &gt; @fired THEN column ELSE value END</c>: the column moves
        /// only for an execution that fired at or after the newest one folded so far.
        /// </summary>
        public Builder Newest(string column, Action<Builder> value)
        {
            Text(", ").Text(column).Text(" = CASE WHEN ").Text(AdoConstants.ColumnLastFiredTime).Text(" > ")
                .Parameter(JobStatusValue.FiredTime).Text(" THEN ").Text(column).Text(" ELSE ");
            value(this);
            return Text(" END");
        }

        /// <summary>
        /// <c>, column = CASE WHEN since &gt; @fired THEN column ELSE value END</c>: the column moves unless
        /// <paramref name="since" /> already holds a later instant. A <see langword="null" /> one compares
        /// unknown, which is the <c>ELSE</c>.
        /// </summary>
        public Builder Later(string column, string since, JobStatusValue value)
        {
            return Text(", ").Text(column).Text(" = CASE WHEN ").Text(since).Text(" > ").Parameter(JobStatusValue.FiredTime)
                .Text(" THEN ").Text(column).Text(" ELSE ").Parameter(value).Text(" END");
        }

        public JobStatusStatement Build() => new(text.ToString(), [.. parameters]);
    }
}
