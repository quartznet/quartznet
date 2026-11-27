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
/// <see cref="LockFreeCompletionRacesTestBase" /> on PostgreSQL, the database of the report (#4399).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class LockFreeCompletionRacesPostgresTest : LockFreeCompletionRacesTestBase
{
    public LockFreeCompletionRacesPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }

    protected override string HoldScheduleRowSql =>
        "SELECT 1 FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group FOR UPDATE";

    protected override string LockWaitersSql =>
        "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'";
}
