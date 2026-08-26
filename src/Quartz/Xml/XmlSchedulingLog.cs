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

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;

namespace Quartz.Xml;

/// <summary>
/// Every event XML scheduling data processing logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 5000-5099 belong to this sub-group of the 5000-5999 range, and are allocated in file
/// order. An id, once given out, is what an operator filters and alerts on, so it is never reused for
/// a different event and never renumbered; <c>LogEventCatalogTest</c> makes a change to one a reviewed
/// diff.
/// </remarks>
internal static partial class XmlSchedulingLog
{
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Parsing XML file: {FileName} with systemId: {SystemId}")]
    public static partial void ParsingFile(this ILogger logger, string fileName, string systemId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Parsing XML from stream with systemId: {SystemId}")]
    public static partial void ParsingStream(this ILogger logger, string? systemId);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Debug, Message = "Found {JobGroupCount} delete job group commands.")]
    public static partial void FoundDeleteJobGroupCommands(this ILogger logger, int jobGroupCount);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug, Message = "Found {TriggerGroupDeleteCount} delete trigger group commands.")]
    public static partial void FoundDeleteTriggerGroupCommands(this ILogger logger, int triggerGroupDeleteCount);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug, Message = "Found {JobsToDeleteCount} delete job commands.")]
    public static partial void FoundDeleteJobCommands(this ILogger logger, int jobsToDeleteCount);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Debug, Message = "Found {TriggersToDelete} delete trigger commands.")]
    public static partial void FoundDeleteTriggerCommands(this ILogger logger, int triggersToDelete);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Debug, Message = "Directive 'overwrite-existing-data' specified as: {Overwrite}")]
    public static partial void OverwriteExistingDataSpecified(this ILogger logger, bool overwrite);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Debug, Message = "Directive 'ignore-duplicates' specified as: {IgnoreDuplicates}")]
    public static partial void IgnoreDuplicatesSpecified(this ILogger logger, bool ignoreDuplicates);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Debug, Message = "Directive 'schedule-trigger-relative-to-replaced-trigger' specified as: {ScheduleRelative}")]
    public static partial void ScheduleTriggerRelativeSpecified(this ILogger logger, bool scheduleRelative);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Debug, Message = "Directive 'overwrite-existing-data' not specified, defaulting to {Overwrite}")]
    public static partial void OverwriteExistingDataDefaulted(this ILogger logger, bool overwrite);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Debug, Message = "Directive 'ignore-duplicates' not specified, defaulting to {IgnoreDuplicates}")]
    public static partial void IgnoreDuplicatesDefaulted(this ILogger logger, bool ignoreDuplicates);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Debug, Message = "Directive 'schedule-trigger-relative-to-replaced-trigger' not specified, defaulting to {ScheduleTriggerRelativeToReplacedTrigger}")]
    public static partial void ScheduleTriggerRelativeDefaulted(this ILogger logger, bool scheduleTriggerRelativeToReplacedTrigger);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Debug, Message = "Found {Count} job definitions.")]
    public static partial void FoundJobDefinitions(this ILogger logger, int count);

    [LoggerMessage(EventId = 5013, Level = LogLevel.Debug, Message = "Parsed job definition: {JobDetail}")]
    public static partial void ParsedJobDefinition(this ILogger logger, IJobDetail jobDetail);

    [LoggerMessage(EventId = 5014, Level = LogLevel.Debug, Message = "Found {TriggerCount} trigger definitions.")]
    public static partial void FoundTriggerDefinitions(this ILogger logger, int triggerCount);

    [LoggerMessage(EventId = 5015, Level = LogLevel.Debug, Message = "Parsed trigger definition: {Trigger}")]
    public static partial void ParsedTriggerDefinition(this ILogger logger, IMutableTrigger trigger);

    [LoggerMessage(EventId = 5016, Level = LogLevel.Warning, Message = "Unable to validate XML with schema: {Message}")]
    public static partial void SchemaValidationUnavailable(this ILogger logger, string message, Exception exception);

    /// <remarks>
    /// The whole message is one placeholder because it is the validating reader's own text, which is
    /// what it always was — this call stood behind a <c>CA2254</c> pragma for exactly that reason.
    /// </remarks>
    [LoggerMessage(EventId = 5017, Level = LogLevel.Warning, Message = "{ValidationMessage}")]
    public static partial void SchemaValidationWarning(this ILogger logger, string validationMessage);

    [LoggerMessage(EventId = 5018, Level = LogLevel.Information, Message = "Adding {JobCount} jobs, {TriggerCount} triggers")]
    public static partial void AddingJobsAndTriggers(this ILogger logger, int jobCount, int triggerCount);

    [LoggerMessage(EventId = 5019, Level = LogLevel.Information, Message = "Removing job: {JobKey}")]
    public static partial void RemovingJob(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 5020, Level = LogLevel.Information, Message = "Not overwriting existing job: {JobKey}")]
    public static partial void NotOverwritingExistingJob(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Information, Message = "Replacing job: {JobKey}")]
    public static partial void ReplacingJob(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 5022, Level = LogLevel.Information, Message = "Adding job: {JobKey}")]
    public static partial void AddingJob(this ILogger logger, JobKey jobKey);

    [LoggerMessage(EventId = 5023, Level = LogLevel.Debug, Message = "Rescheduling job: {JobKey} with updated trigger: {TriggerKey}")]
    public static partial void ReschedulingJob(this ILogger logger, JobKey jobKey, TriggerKey triggerKey);

    [LoggerMessage(EventId = 5024, Level = LogLevel.Information, Message = "Not overwriting existing trigger: {Key}")]
    public static partial void NotOverwritingExistingTriggerByKey(this ILogger logger, TriggerKey key);

    [LoggerMessage(EventId = 5025, Level = LogLevel.Debug, Message = "Scheduling job: {JobKey} with trigger: {TriggerKey}")]
    public static partial void SchedulingJob(this ILogger logger, JobKey jobKey, TriggerKey triggerKey);

    /// <remarks>
    /// Both places that add a trigger and find one already there raise this. They spelled the same
    /// sentence with different spacing, which would have made it two events for one thing — 5027 is
    /// deliberately never allocated.
    /// </remarks>
    [LoggerMessage(EventId = 5026, Level = LogLevel.Debug, Message = "Adding trigger: {TriggerKey} for job: {JobKey} failed because the trigger already existed. This is likely due to a race condition between multiple instances in the cluster. Will try to reschedule instead.")]
    public static partial void TriggerAlreadyExistedWillReschedule(this ILogger logger, TriggerKey triggerKey, JobKey jobKey);

    [LoggerMessage(EventId = 5028, Level = LogLevel.Information, Message = "Not overwriting existing trigger: {JobKey}")]
    public static partial void NotOverwritingExistingTrigger(this ILogger logger, TriggerKey jobKey);

    [LoggerMessage(EventId = 5029, Level = LogLevel.Warning, Message = "Possibly duplicately named ({TriggerKey}) trigger in configuration, this can be caused by not having a fixed job key for targeted jobs")]
    public static partial void DuplicatelyNamedTrigger(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 5030, Level = LogLevel.Debug, Message = "Using relative scheduling for trigger with key {TriggerKey}")]
    public static partial void UsingRelativeScheduling(this ILogger logger, TriggerKey triggerKey);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Information, Message = "Deleting all jobs in ALL groups.")]
    public static partial void DeletingAllJobsInAllGroups(this ILogger logger);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Information, Message = "Deleting all jobs in group: {Group}")]
    public static partial void DeletingAllJobsInGroup(this ILogger logger, string group);

    [LoggerMessage(EventId = 5033, Level = LogLevel.Information, Message = "Deleting all triggers in ALL groups.")]
    public static partial void DeletingAllTriggersInAllGroups(this ILogger logger);

    [LoggerMessage(EventId = 5034, Level = LogLevel.Information, Message = "Deleting all triggers in group: {Group}")]
    public static partial void DeletingAllTriggersInGroup(this ILogger logger, string group);

    [LoggerMessage(EventId = 5035, Level = LogLevel.Information, Message = "Deleting job: {Key}")]
    public static partial void DeletingJob(this ILogger logger, JobKey key);

    [LoggerMessage(EventId = 5036, Level = LogLevel.Information, Message = "Deleting trigger: {Key}")]
    public static partial void DeletingTrigger(this ILogger logger, TriggerKey key);
}
