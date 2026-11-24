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
/// Which of the four run status updates one recorded execution folds in through.
/// </summary>
/// <remarks>
/// Chosen by the store from the execution rather than decided in SQL, because Firebird refuses a
/// parameter compared with a literal and every dialect spells a boolean differently. Four rather than
/// three: a failure the trigger retries moves the last failure, and a cancelled run does not, as
/// <see cref="RunTally" /> has it.
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
/// What a placeholder of a run status update is bound to.
/// </summary>
internal enum JobStatusValue
{
    SchedulerName,

    /// <summary>The group of the status's key: the job's, or the trigger's.</summary>
    KeyGroup,

    /// <summary>The name of the status's key: the job's, or the trigger's.</summary>
    KeyName,

    FiredTime,
    Result,
    RunTime,
    InstanceName,
    EntryId,
    Summary,
    FailureMessage
}

/// <summary>
/// A table a run status is kept in, and the key it is kept by: <c>QRTZ_JOB_STATUS</c> by job, or
/// <c>QRTZ_TRIGGER_STATUS</c> by trigger. The value columns are the same in both.
/// </summary>
internal sealed class RunStatusTable
{
    /// <summary><c>QRTZ_JOB_STATUS</c>, keyed by the job.</summary>
    public static readonly RunStatusTable Job = new(
        AdoConstants.TableJobStatus,
        AdoConstants.TableJobDetails,
        "j",
        AdoConstants.ColumnJobGroup,
        AdoConstants.ColumnJobName,
        SqlParameters.JobGroup,
        SqlParameters.JobName);

    /// <summary><c>QRTZ_TRIGGER_STATUS</c>, keyed by the trigger.</summary>
    public static readonly RunStatusTable Trigger = new(
        AdoConstants.TableTriggerStatus,
        AdoConstants.TableTriggers,
        "t",
        AdoConstants.ColumnTriggerGroup,
        AdoConstants.ColumnTriggerName,
        SqlParameters.TriggerGroup,
        SqlParameters.TriggerName);

    private RunStatusTable(string table, string ownerTable, string ownerAlias, string groupColumn, string nameColumn, string groupParameter, string nameParameter)
    {
        Table = table;
        OwnerTable = ownerTable;
        OwnerAlias = ownerAlias;
        GroupColumn = groupColumn;
        NameColumn = nameColumn;
        GroupParameter = groupParameter;
        NameParameter = nameParameter;
    }

    /// <summary>The table, without the prefix.</summary>
    public string Table { get; }

    /// <summary>
    /// The table the key belongs to, without the prefix: a status whose key is gone from it is an orphan the
    /// sweep deletes.
    /// </summary>
    public string OwnerTable { get; }

    /// <summary>The alias the orphan sweep reads <see cref="OwnerTable" /> under.</summary>
    public string OwnerAlias { get; }

    public string GroupColumn { get; }

    public string NameColumn { get; }

    /// <summary>The parameter the key's group is bound to, in every statement but the per-run update.</summary>
    public string GroupParameter { get; }

    /// <summary>The parameter the key's name is bound to, in every statement but the per-run update.</summary>
    public string NameParameter { get; }
}

/// <summary>
/// One update of a run status table, the SQL and its placeholders in the order the SQL names them.
/// </summary>
/// <remarks>
/// <para>
/// The fold of <see cref="RunTally" />, one row at a time in the database:
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
internal sealed class RunStatusStatement
{
    private RunStatusStatement(string sql, (string Name, JobStatusValue Value)[] parameters)
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

    /// <summary>Builds the update of one table for one kind of execution.</summary>
    internal static RunStatusStatement Update(RunStatusTable table, JobStatusUpdate kind)
    {
        Builder sql = new();

        sql.Text("UPDATE ").Text(StdAdoConstants.TablePrefixSubst).Text(table.Table)
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
            .Text(" AND ").Text(table.GroupColumn).Text(" = ").Parameter(JobStatusValue.KeyGroup)
            .Text(" AND ").Text(table.NameColumn).Text(" = ").Parameter(JobStatusValue.KeyName);

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

        public RunStatusStatement Build() => new(text.ToString(), [.. parameters]);
    }
}
