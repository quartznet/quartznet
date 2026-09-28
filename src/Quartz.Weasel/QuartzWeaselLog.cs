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

using JasperFx;

using Microsoft.Extensions.Logging;

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// Every event the Weasel integration logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 10000-10099 belong to this area, and the dialect packages raise theirs through this class
/// rather than holding a catalogue of their own, so one range covers all of them. An id, once given out,
/// is what an operator filters and alerts on; <c>LogEventCatalogTest</c> makes a change to one a
/// reviewed diff.
/// </remarks>
internal static partial class QuartzWeaselLog
{
    [LoggerMessage(EventId = 10000, Level = LogLevel.Information, Message = "Applied the schema changes scheduler '{SchedulerName}' needs to {Database} ({Difference})")]
    public static partial void SchemaApplied(this ILogger logger, string schedulerName, Uri database, SchemaPatchDifference difference);

    [LoggerMessage(EventId = 10001, Level = LogLevel.Debug, Message = "The schema of scheduler '{SchedulerName}' in {Database} already matches the model")]
    public static partial void SchemaUnchanged(this ILogger logger, string schedulerName, Uri database);

    [LoggerMessage(EventId = 10002, Level = LogLevel.Information, Message = "Not applying the schema of scheduler '{SchedulerName}' at startup, because AutoCreate is {AutoCreate}; the store still validates it")]
    public static partial void SchemaApplySkipped(this ILogger logger, string schedulerName, AutoCreate autoCreate);

    [LoggerMessage(EventId = 10003, Level = LogLevel.Error, Message = "Could not apply the schema of scheduler '{SchedulerName}' at startup; continuing because the active JasperFx profile's ResourceMigrationFailureMode is ContinueOnFailures")]
    public static partial void SchemaApplyFailedContinuing(this ILogger logger, string schedulerName, Exception exception);

    [LoggerMessage(EventId = 10004, Level = LogLevel.Information, Message = "Executing schema change for scheduler '{SchedulerName}': {Sql}")]
    public static partial void SchemaChange(this ILogger logger, string schedulerName, string sql);

    [LoggerMessage(EventId = 10005, Level = LogLevel.Warning, Message = "Destructive schema change for scheduler '{SchedulerName}': {Description}")]
    public static partial void DestructiveChange(this ILogger logger, string schedulerName, string description);

    // Debug rather than Information: what is withheld is something an application put on Quartz's tables
    // on purpose, and it is withheld on every start for as long as it is there.
    [LoggerMessage(EventId = 10006, Level = LogLevel.Debug, Message = "Keeping objects the model of scheduler '{SchedulerName}' does not declare: {Description}")]
    public static partial void WithheldDrop(this ILogger logger, string schedulerName, string description);

    [LoggerMessage(EventId = 10007, Level = LogLevel.Error, Message = "Schema change for scheduler '{SchedulerName}' failed: {Sql}")]
    public static partial void SchemaChangeFailed(this ILogger logger, string schedulerName, string sql, Exception exception);

    [LoggerMessage(EventId = 10008, Level = LogLevel.Warning, Message = "Applying the schema of scheduler '{SchedulerName}' to {Database} failed on attempt {Attempt} of {Attempts}; reading the schema again in case another process was applying it at the same time")]
    public static partial void SchemaApplyRetrying(this ILogger logger, string schedulerName, Uri database, int attempt, int attempts, Exception exception);

    [LoggerMessage(EventId = 10009, Level = LogLevel.Information, Message = "Another process finished applying the schema of scheduler '{SchedulerName}' to {Database} first")]
    public static partial void SchemaAppliedByAnotherProcess(this ILogger logger, string schedulerName, Uri database);
}
