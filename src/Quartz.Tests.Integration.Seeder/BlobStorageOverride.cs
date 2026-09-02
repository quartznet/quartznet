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

using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// Which triggers this run stores as a blob, and why it has to be arranged rather than merely done.
/// </summary>
/// <remarks>
/// <para>
/// <c>QRTZ_BLOB_TRIGGERS</c> holds a trigger 3.x had no persistence delegate for — in practice an
/// application's own <see cref="ITrigger" /> implementation. The seeder cannot use one: the blob would
/// name a type in <em>this</em> assembly, and a 4.0 process reading it has no such assembly, so the
/// row would prove only that an unresolvable blob stays unresolvable.
/// </para>
/// <para>
/// So the seeder stores one trigger of each shipped family as a blob instead, by declining the
/// delegate for a single trigger group. The bytes are then a stock Quartz trigger written by the
/// released 3.20 serializer, which is exactly the payload the fixtures under
/// <c>src/Quartz.Tests.Unit/TestData/Legacy/3.20/</c> want and exactly what a 4.0 reader has to
/// understand. What is <em>not</em> covered, and is not coverable from here, is a blob naming a type
/// the reader does not ship.
/// </para>
/// <para>
/// It is one override, of one <c>protected virtual</c> method, on each of 3.20's six shipped driver
/// delegates. Everything else — every statement, every parameter binding — is the released version's.
/// </para>
/// </remarks>
internal static class BlobStorageOverride
{
    /// <summary>
    /// The trigger group whose members go to <c>QRTZ_BLOB_TRIGGERS</c> whatever family they belong to.
    /// </summary>
    public const string Group = "blob";

    /// <summary>
    /// The families blob-stored for a given serializer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four rather than five under Newtonsoft, and the missing one is a defect in the released 3.20
    /// rather than a choice: with the settings a 3.x deployment gets by default —
    /// <c>JsonObjectSerializer.RegisterTriggerConverters</c> is <c>false</c> there — a
    /// <c>DailyTimeIntervalTriggerImpl</c> is written as a plain object graph whose
    /// <c>StartTimeOfDay</c> and <c>EndTimeOfDay</c> are <c>Quartz.TimeOfDay</c> objects, and
    /// <c>TimeOfDay</c> has neither a parameterless constructor nor a <c>[JsonConstructor]</c>. So
    /// 3.20 cannot read that blob back either: <c>SelectTrigger</c> throws
    /// "Unable to find a constructor to use for type Quartz.TimeOfDay" against a row 3.20 itself just
    /// wrote.
    /// </para>
    /// <para>
    /// There is therefore no such row in the wild that ever worked, and nothing for the rehearsal to
    /// assert about 4.0 reading one. The System.Text.Json run stores all five, because its
    /// discriminated form round-trips.
    /// </para>
    /// </remarks>
    public static string[] Families(string serializer)
    {
        return serializer == "json"
            ? ["simple", "cron", "calendar-interval", "recurrence"]
            : ["simple", "cron", "calendar-interval", "daily-time-interval", "recurrence"];
    }
}

internal sealed class BlobForcingSQLiteDelegate : SQLiteDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}

internal sealed class BlobForcingSqlServerDelegate : SqlServerDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}

internal sealed class BlobForcingPostgreSQLDelegate : PostgreSQLDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}

internal sealed class BlobForcingMySQLDelegate : MySQLDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}

internal sealed class BlobForcingOracleDelegate : OracleDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}

internal sealed class BlobForcingFirebirdDelegate : FirebirdDelegate
{
    protected override ITriggerPersistenceDelegate? FindTriggerPersistenceDelegate(IOperableTrigger trigger)
    {
        return trigger.Key.Group == BlobStorageOverride.Group ? null : base.FindTriggerPersistenceDelegate(trigger);
    }
}
