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

using System.Diagnostics;

using Npgsql;

namespace Quartz.Tests.Integration.MixedVersionNode;

/// <summary>
/// The table every execution on either node is written to, which is what the test's assertions read.
/// </summary>
/// <remarks>
/// A plain table the test creates, not a Quartz one: an execution is recorded by the job that ran, so
/// what it says happened is what happened, whatever the store's own rows say afterwards. Start and end
/// are <see cref="Stopwatch" /> timestamps, which both processes on one machine read from the same
/// monotonic clock, so an overlap between two nodes' executions is a comparison of two numbers.
/// </remarks>
internal static class Runs
{
    private static string connectionString = "";
    private static string schedulerName = "";
    private static string insert = "";
    private static string selectProgress = "";

    public static void Configure(NodeOptions options)
    {
        connectionString = options.ConnectionString;
        schedulerName = options.SchedulerName;

        insert = $"INSERT INTO {options.RunsTable} "
                 + "(trigger_group, trigger_name, job_group, job_name, fire_instance_id, node, scheduled_fire_utc, fired_utc, "
                 + "started_utc, started, ended, recovering, progress, progress_message) VALUES "
                 + "(@triggerGroup, @triggerName, @jobGroup, @jobName, @fireInstanceId, @node, @scheduledFireUtc, @firedUtc, "
                 + "@startedUtc, @started, @ended, @recovering, @progress, @progressMessage)";

        selectProgress = $"SELECT progress, progress_message FROM {options.TablePrefix}fired_triggers "
                         + "WHERE sched_name = @schedulerName AND entry_id = @entryId";
    }

    public static async Task Record(IJobExecutionContext context, long startedUtc, long started, long ended, FireProgress? progress)
    {
        // Not the job's token: a firing that ran has to be on record even if the node is stopping,
        // or it would read as never having run.
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

        await using NpgsqlCommand command = new(insert, connection);
        command.Parameters.AddWithValue("triggerGroup", context.Trigger.Key.Group);
        command.Parameters.AddWithValue("triggerName", context.Trigger.Key.Name);
        command.Parameters.AddWithValue("jobGroup", context.JobDetail.Key.Group);
        command.Parameters.AddWithValue("jobName", context.JobDetail.Key.Name);
        command.Parameters.AddWithValue("fireInstanceId", context.FireInstanceId);
        command.Parameters.AddWithValue("node", context.Scheduler.SchedulerInstanceId);
        command.Parameters.AddWithValue("scheduledFireUtc", (object?) context.ScheduledFireTimeUtc?.UtcTicks ?? DBNull.Value);
        command.Parameters.AddWithValue("firedUtc", context.FireTimeUtc.UtcTicks);
        command.Parameters.AddWithValue("startedUtc", startedUtc);
        command.Parameters.AddWithValue("started", started);
        command.Parameters.AddWithValue("ended", ended);
        command.Parameters.AddWithValue("recovering", context.Recovering);
        command.Parameters.AddWithValue("progress", (object?) progress?.Percent ?? DBNull.Value);
        command.Parameters.AddWithValue("progressMessage", (object?) progress?.Message ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// What this firing's own <c>QRTZ_FIRED_TRIGGERS</c> row says its progress is, read straight from
    /// the column.
    /// </summary>
    public static async Task<FireProgress> ReadProgress(string fireInstanceId)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using NpgsqlCommand command = new(selectProgress, connection);
        command.Parameters.AddWithValue("schedulerName", schedulerName);
        command.Parameters.AddWithValue("entryId", fireInstanceId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return new FireProgress(null, "<no fired-trigger row>");
        }

        return new FireProgress(
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}

/// <summary>A progress value as a fired-trigger row holds it.</summary>
internal sealed record FireProgress(int? Percent, string? Message);
