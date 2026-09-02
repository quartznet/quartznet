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

// Stated rather than inherited: this file is also compiled into Quartz.Tests.Integration, which
// disables nullable reference types the way every test project here does.
#nullable enable

using System.Text.Json.Serialization;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// What a 3.20 seeding run put in the database, written to <c>seed.json</c> beside it.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into <c>Quartz.Tests.Integration</c> as well, by source rather than by
/// assembly reference: the seeder builds against the released <c>Quartz</c> 3.20.0 package, whose
/// assembly identity is this repository's own, so nothing may reference both. Everything here is
/// therefore BCL-only, and must stay that way — a Quartz type in this file would break the include.
/// </para>
/// <para>
/// The manifest is the assertions' vocabulary. It records what 3.20 <em>stored</em>, read back out of
/// the 3.20 scheduler and out of the tables themselves, rather than what the seeder asked for: the
/// question the rehearsal asks is whether 4.0 sees what 3.20 wrote, and a manifest built from the
/// seeder's intentions could agree with 4.0 while both disagreed with the database.
/// </para>
/// </remarks>
internal sealed class SeedManifest
{
    /// <summary>The released package version that wrote the data. Never a floating one.</summary>
    public string QuartzVersion { get; set; } = "";

    public string Dialect { get; set; } = "";

    /// <summary><c>json</c> for Newtonsoft, <c>stj</c> for System.Text.Json.</summary>
    public string Serializer { get; set; } = "";

    public string TablePrefix { get; set; } = "";

    public string SchedulerName { get; set; } = "";

    public string InstanceId { get; set; } = "";

    /// <summary>
    /// The <c>JOB_CLASS_NAME</c> 3.20 stored, read back out of the column. It names an assembly a 4.0
    /// process does not have, which is what makes it the thing to hand
    /// <c>UseTypeLoader(o =&gt; o.Map(…))</c> — the same move an application whose job types were
    /// renamed has to make.
    /// </summary>
    public string JobTypeName { get; set; } = "";

    public DateTimeOffset CapturedUtc { get; set; }

    public List<SeededJob> Jobs { get; set; } = [];

    public List<SeededTrigger> Triggers { get; set; } = [];

    public List<SeededCalendar> Calendars { get; set; } = [];

    /// <summary>The <c>QRTZ_PAUSED_TRIGGER_GRPS</c> rows, read out of the table itself.</summary>
    public List<string> PausedTriggerGroups { get; set; } = [];

    /// <summary>
    /// The job groups the seeder asked 3.20 to pause. 3.x does not persist these anywhere — its ADO
    /// store's <c>IsJobGroupPaused</c> returns a hard-coded false — so this is a record of an
    /// instruction that left no trace, and the rehearsal asserts exactly that.
    /// </summary>
    public List<string> PausedJobGroups { get; set; } = [];

    public SeededFiredTrigger? OrphanedFiredTrigger { get; set; }

    /// <summary>The blob-column dumps written beside the manifest, by file name.</summary>
    public List<string> BlobFixtures { get; set; } = [];

    /// <summary>
    /// Fully qualified because both Quartz versions this file compiles against declare a
    /// <c>JsonSerializerOptions</c> of their own in the <c>Quartz</c> namespace, which encloses this
    /// one and so wins the simple name.
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed class SeededJob
{
    public string Name { get; set; } = "";

    public string Group { get; set; } = "";

    public string? Description { get; set; }

    public bool Durable { get; set; }

    public bool RequestsRecovery { get; set; }

    public bool ConcurrentExecutionDisallowed { get; set; }

    public List<SeededDataValue> JobDataMap { get; set; } = [];
}

/// <summary>
/// One job data map entry, as its kind and its invariant text form.
/// </summary>
/// <remarks>
/// The text form rather than the value because JSON has no way to tell a <see cref="decimal" /> from
/// a <see cref="double" />, and because the point of the entry is that 4.0's typed accessor coerces
/// the stored shape back to the type 3.20 was handed. <see cref="Kind" /> names which accessor the
/// assertion should use.
/// </remarks>
internal sealed class SeededDataValue
{
    public string Key { get; set; } = "";

    /// <summary>
    /// One of <c>string</c>, <c>bool</c>, <c>int</c>, <c>long</c>, <c>double</c>, <c>float</c>,
    /// <c>decimal</c>, <c>char</c>, <c>dateTime</c>, <c>dateTimeOffset</c>, <c>timeSpan</c>,
    /// <c>guid</c>, <c>dateOnly</c>, <c>timeOnly</c>, <c>enum</c>, <c>dictionary</c> or
    /// <c>outsideTheWriteGate</c>.
    /// </summary>
    public string Kind { get; set; } = "";

    public string? Text { get; set; }

    /// <summary>Set for <c>dictionary</c> entries; null for every other kind.</summary>
    public Dictionary<string, string>? Entries { get; set; }
}

internal sealed class SeededTrigger
{
    public string Name { get; set; } = "";

    public string Group { get; set; } = "";

    /// <summary>The <c>TRIGGER_TYPE</c> discriminator the row carries, read out of the table.</summary>
    public string TriggerType { get; set; } = "";

    /// <summary>The <c>TRIGGER_STATE</c> the row carries, read out of the table.</summary>
    public string TriggerState { get; set; } = "";

    public string JobName { get; set; } = "";

    public string JobGroup { get; set; } = "";

    public string? Description { get; set; }

    public string? CalendarName { get; set; }

    public int Priority { get; set; }

    public int MisfireInstruction { get; set; }

    public string? ExecutionGroup { get; set; }

    public string? PreferredNode { get; set; }

    /// <summary>
    /// Whether the rehearsal should wait for this trigger to fire. False for the ones that have no
    /// next fire time left — the crashed firing's trigger, and anything born paused.
    /// </summary>
    public bool ExpectFires { get; set; }

    public SeededSchedule Schedule { get; set; } = new();
}

/// <summary>
/// The schedule fields of whichever family the trigger belongs to. One flat bag rather than a
/// hierarchy, because the manifest is read by a test that switches on <see cref="Kind" /> anyway and
/// polymorphic JSON would buy nothing but a discriminator of its own.
/// </summary>
internal sealed class SeededSchedule
{
    /// <summary>
    /// <c>simple</c>, <c>cron</c>, <c>calendarInterval</c>, <c>dailyTimeInterval</c> or
    /// <c>recurrence</c>.
    /// </summary>
    public string Kind { get; set; } = "";

    public int? RepeatCount { get; set; }

    public long? RepeatIntervalMilliseconds { get; set; }

    public int? RepeatInterval { get; set; }

    public string? RepeatIntervalUnit { get; set; }

    public string? CronExpression { get; set; }

    public string? RecurrenceRule { get; set; }

    public string? TimeZoneId { get; set; }

    public string? StartTimeOfDay { get; set; }

    public string? EndTimeOfDay { get; set; }

    public List<string>? DaysOfWeek { get; set; }

    public bool? PreserveHourOfDayAcrossDaylightSavings { get; set; }

    public bool? SkipDayIfHourDoesNotExist { get; set; }
}

internal sealed class SeededCalendar
{
    public string Name { get; set; } = "";

    /// <summary>The calendar's simple type name as 3.20 knew it, for example <c>HolidayCalendar</c>.</summary>
    public string Kind { get; set; } = "";

    public string? Description { get; set; }

    public bool HasBaseCalendar { get; set; }

    public string? BaseCalendarKind { get; set; }

    /// <summary>
    /// Instants 3.20's own calendar was asked about, with the answers it gave. The rehearsal asks 4.0
    /// the same questions, so a calendar whose blob deserializes into something that schedules
    /// differently fails here rather than silently changing when jobs run.
    /// </summary>
    public List<SeededCalendarProbe> Probes { get; set; } = [];
}

internal sealed class SeededCalendarProbe
{
    public DateTimeOffset Instant { get; set; }

    public bool Included { get; set; }
}

internal sealed class SeededFiredTrigger
{
    public string FireInstanceId { get; set; } = "";

    public string InstanceName { get; set; } = "";

    public string TriggerName { get; set; } = "";

    public string TriggerGroup { get; set; } = "";

    public string JobName { get; set; } = "";

    public string JobGroup { get; set; } = "";

    public string State { get; set; } = "";

    public bool RequestsRecovery { get; set; }
}
