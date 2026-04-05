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

namespace Quartz.Plugin.Json;

internal sealed class JsonJobSchedulingData
{
    public JsonPreProcessingCommands? PreProcessingCommands { get; set; }
    public JsonProcessingDirectives? ProcessingDirectives { get; set; }
    public JsonScheduleData? Schedule { get; set; }
}

internal sealed class JsonPreProcessingCommands
{
    public List<string>? DeleteJobsInGroup { get; set; }
    public List<string>? DeleteTriggersInGroup { get; set; }
    public List<JsonDeleteJobCommand>? DeleteJobs { get; set; }
    public List<JsonDeleteTriggerCommand>? DeleteTriggers { get; set; }
}

internal sealed class JsonDeleteJobCommand
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
}

internal sealed class JsonDeleteTriggerCommand
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
}

internal sealed class JsonProcessingDirectives
{
    public bool OverWriteExistingData { get; set; } = true;
    public bool IgnoreDuplicates { get; set; }
    public bool ScheduleTriggerRelativeToReplacedTrigger { get; set; }
}

internal sealed class JsonScheduleData
{
    public List<JsonFileJobDefinition>? Jobs { get; set; }
    public List<JsonFileTriggerDefinition>? Triggers { get; set; }
}

internal sealed class JsonFileJobDefinition
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string JobType { get; set; } = "";
    public string? Description { get; set; }
    public bool Durable { get; set; }
    public bool Recover { get; set; }
    public Dictionary<string, string>? JobDataMap { get; set; }
}

internal sealed class JsonFileTriggerDefinition
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string JobName { get; set; } = "";
    public string? JobGroup { get; set; }
    public string? Description { get; set; }
    public int? Priority { get; set; }
    public string? CalendarName { get; set; }
    public string? StartTime { get; set; }
    public int? StartTimeSecondsInFuture { get; set; }
    public string? EndTime { get; set; }
    public Dictionary<string, string>? JobDataMap { get; set; }

    public JsonFileSimpleSchedule? Simple { get; set; }
    public JsonFileCronSchedule? Cron { get; set; }
    public JsonFileCalendarIntervalSchedule? CalendarInterval { get; set; }
    public JsonFileDailyTimeIntervalSchedule? DailyTimeInterval { get; set; }
}

internal sealed class JsonFileSimpleSchedule
{
    public int RepeatCount { get; set; }
    public string Interval { get; set; } = "00:00:00";
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonFileCronSchedule
{
    public string Expression { get; set; } = "";
    public string? TimeZone { get; set; }
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonFileCalendarIntervalSchedule
{
    public int RepeatInterval { get; set; }
    public string RepeatIntervalUnit { get; set; } = "Day";
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonFileDailyTimeIntervalSchedule
{
    public int RepeatInterval { get; set; } = 1;
    public string RepeatIntervalUnit { get; set; } = "Minute";
    public int RepeatCount { get; set; } = -1;
    public string? StartTimeOfDay { get; set; }
    public string? EndTimeOfDay { get; set; }
    public List<string>? DaysOfWeek { get; set; }
    public string? TimeZone { get; set; }
    public string? MisfireInstruction { get; set; }
}
