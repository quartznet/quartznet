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

using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Job;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Test for PostgreSQL lock handler race condition.
/// </summary>
[NonParallelizable]
public class PostgreSQLLockTest
{
    [Test]
    [Category("db-postgres")]
    public async Task TestParallelJobScheduling_ShouldNotCauseTransactionAbort()
    {
        // This test reproduces the race condition where multiple threads try to insert
        // into the locks table simultaneously, which would cause a PK violation and 
        // transaction abort without the PostgreSQL-specific fix

        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "TestScheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.useProperties"] = "false",
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.PostgreSQLDelegate, Quartz",
            ["quartz.dataSource.default.connectionString"] = TestConstants.PostgresConnectionString,
            ["quartz.dataSource.default.provider"] = "Npgsql",
            ["quartz.threadPool.maxConcurrency"] = "10"
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            // Clear the locks table directly to ensure the INSERT race condition is exercised
            // Without this, scheduler.Clear() would acquire locks and pre-populate the table
            using (var conn = new Npgsql.NpgsqlConnection(TestConstants.PostgresConnectionString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM QRTZ_LOCKS";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await scheduler.Start();

            // Schedule multiple jobs in parallel to trigger the race condition
            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                int jobIndex = i;
                var task = Task.Run(async () =>
                {
                    var job = JobBuilder.Create<NoOpJob>()
                        .WithIdentity($"job{jobIndex}", "testGroup")
                        .Build();

                    var trigger = TriggerBuilder.Create()
                        .WithIdentity($"trigger{jobIndex}", "testGroup")
                        .StartNow()
                        .Build();

                    await scheduler.ScheduleJob(job, trigger);
                });
                tasks.Add(task);
            }

            // Wait for all scheduling operations to complete
            // If we get here without an exception, the test passes
            await Task.WhenAll(tasks);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }
}
