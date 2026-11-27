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

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// <see cref="LockFreeCompletionRacesTestBase" /> on SQL Server with <c>READ_COMMITTED_SNAPSHOT ON</c>, where a
/// read does not wait for a writer, as on PostgreSQL: the completion reads the spent row and waits at its first
/// write.
/// </summary>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class LockFreeCompletionRacesSqlServerSnapshotTest : LockFreeCompletionRacesTestBase
{
    public LockFreeCompletionRacesSqlServerSnapshotTest() : base(ClusteredTestDatabase.SqlServerSnapshot)
    {
    }

    protected override string HoldScheduleRowSql =>
        "SELECT 1 FROM QRTZ_SIMPLE_TRIGGERS WITH (UPDLOCK, ROWLOCK) WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";

    protected override string LockWaitersSql =>
        "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID()";
}
