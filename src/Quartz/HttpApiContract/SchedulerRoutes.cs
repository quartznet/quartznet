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

using System.Collections.ObjectModel;

namespace Quartz.HttpApiContract;

/// <summary>
/// Every route of the HTTP API, in one closed table.
/// </summary>
/// <remarks>
/// <para>
/// The server maps its endpoints from these and <c>HttpScheduler</c> builds its requests from them, so a
/// path is spelled once. <see cref="SchedulerOperations" /> holds what each route does; this says where it
/// is. A route is named for its endpoint, which is also its OpenAPI operation id, so the names are the
/// ones the API has always published — <see cref="ScheduleJob" /> is a trigger route and
/// <see cref="PauseJobs" /> is the group-matcher form, for that reason.
/// </para>
/// <para>
/// <see cref="Match" /> routes a method and a path back to the route, with the rules ASP.NET Core applies
/// to these templates, which is what a carrier that is not ASP.NET Core dispatches with.
/// <c>WireCarrierEquivalenceTest</c> holds the table, the endpoints the server maps and the requests
/// <c>HttpScheduler</c> sends to one another.
/// </para>
/// </remarks>
internal static class SchedulerRoutes
{
    private const string Get = "GET";
    private const string Post = "POST";
    private const string Delete = "DELETE";

    private const string Scheduler = "schedulers/{schedulerName}";
    private const string Job = Jobs + "/{jobGroup}/{jobName}";
    private const string Jobs = Scheduler + "/jobs";
    private const string Trigger = Triggers + "/{triggerGroup}/{triggerName}";
    private const string Triggers = Scheduler + "/triggers";
    private const string Calendar = Calendars + "/{calendarName}";
    private const string Calendars = Scheduler + "/calendars";
    private const string History = Scheduler + "/history";

    // Declared before the routes: static fields initialize in the order they are written, and each route
    // below registers itself here as it is created.
    private static readonly List<WireRoute> routes = [];

    // --- Schedulers ------------------------------------------------------------------------------------

    public static readonly WireRoute GetAllSchedulers = Route(nameof(GetAllSchedulers), Get, "schedulers");
    public static readonly WireRoute GetSchedulerDetails = Route(nameof(GetSchedulerDetails), Get, Scheduler);
    public static readonly WireRoute GetSchedulerContext = Route(nameof(GetSchedulerContext), Get, Scheduler + "/context");
    public static readonly WireRoute Start = Route(nameof(Start), Post, Scheduler + "/start");
    public static readonly WireRoute Standby = Route(nameof(Standby), Post, Scheduler + "/standby");
    public static readonly WireRoute Shutdown = Route(nameof(Shutdown), Post, Scheduler + "/shutdown");
    public static readonly WireRoute Clear = Route(nameof(Clear), Post, Scheduler + "/clear");
    public static readonly WireRoute PauseAll = Route(nameof(PauseAll), Post, Scheduler + "/pause-all");
    public static readonly WireRoute ResumeAll = Route(nameof(ResumeAll), Post, Scheduler + "/resume-all");
    public static readonly WireRoute GetClusterNodes = Route(nameof(GetClusterNodes), Get, Scheduler + "/nodes");
    public static readonly WireRoute StreamEvents = Route(nameof(StreamEvents), Get, Scheduler + "/events");
    public static readonly WireRoute QueryExecutionHistory = Route(nameof(QueryExecutionHistory), Get, History + "/executions");
    public static readonly WireRoute GetExecution = Route(nameof(GetExecution), Get, History + "/executions/{entryId}");
    public static readonly WireRoute QueryMisfireHistory = Route(nameof(QueryMisfireHistory), Get, History + "/misfires");
    public static readonly WireRoute CountMisfires = Route(nameof(CountMisfires), Get, History + "/misfires/count");

    // Under history, beside the rows the statuses are folded from. A host that predates them answers 404
    // without problem details, which is what tells a reader the host keeps no status.
    public static readonly WireRoute QueryJobRunStatuses = Route(nameof(QueryJobRunStatuses), Get, History + "/job-status");
    public static readonly WireRoute GetJobRunStatus = Route(nameof(GetJobRunStatus), Get, History + "/job-status/{jobGroup}/{jobName}");
    public static readonly WireRoute FetchJobRunStatuses = Route(nameof(FetchJobRunStatuses), Post, History + "/job-status/fetch");
    public static readonly WireRoute GetExecutionLimits = Route(nameof(GetExecutionLimits), Get, Scheduler + "/execution-limits");
    public static readonly WireRoute SetExecutionLimits = Route(nameof(SetExecutionLimits), Post, Scheduler + "/execution-limits");
    public static readonly WireRoute ClearExecutionLimits = Route(nameof(ClearExecutionLimits), Delete, Scheduler + "/execution-limits");

    // --- Jobs ------------------------------------------------------------------------------------------

    public static readonly WireRoute QueryJobs = Route(nameof(QueryJobs), Get, Jobs);
    public static readonly WireRoute FetchJobs = Route(nameof(FetchJobs), Post, Jobs + "/fetch");
    public static readonly WireRoute GetJobDetails = Route(nameof(GetJobDetails), Get, Job);
    public static readonly WireRoute CheckJobExists = Route(nameof(CheckJobExists), Get, Job + "/exists");
    public static readonly WireRoute GetJobTriggers = Route(nameof(GetJobTriggers), Get, Job + "/triggers");
    public static readonly WireRoute QueryFireInstances = Route(nameof(QueryFireInstances), Get, Jobs + "/fire-instances");
    public static readonly WireRoute PauseJob = Route(nameof(PauseJob), Post, Job + "/pause");
    public static readonly WireRoute PauseJobs = Route(nameof(PauseJobs), Post, Jobs + "/pause");
    public static readonly WireRoute PauseJobKeys = Route(nameof(PauseJobKeys), Post, Jobs + "/keys/pause");
    public static readonly WireRoute ResumeJob = Route(nameof(ResumeJob), Post, Job + "/resume");
    public static readonly WireRoute ResumeJobs = Route(nameof(ResumeJobs), Post, Jobs + "/resume");
    public static readonly WireRoute ResumeJobKeys = Route(nameof(ResumeJobKeys), Post, Jobs + "/keys/resume");
    public static readonly WireRoute TriggerJob = Route(nameof(TriggerJob), Post, Job + "/trigger");
    public static readonly WireRoute InterruptJob = Route(nameof(InterruptJob), Post, Job + "/interrupt");
    public static readonly WireRoute InterruptJobInstance = Route(nameof(InterruptJobInstance), Post, Jobs + "/interrupt/{fireInstanceId}");
    public static readonly WireRoute DeleteJob = Route(nameof(DeleteJob), Delete, Job);
    public static readonly WireRoute DeleteJobs = Route(nameof(DeleteJobs), Post, Jobs + "/delete");
    public static readonly WireRoute DeleteJobsByGroup = Route(nameof(DeleteJobsByGroup), Post, Jobs + "/delete-by-group");
    public static readonly WireRoute AddJob = Route(nameof(AddJob), Post, Jobs);
    public static readonly WireRoute QueryJobGroups = Route(nameof(QueryJobGroups), Get, Jobs + "/groups");
    public static readonly WireRoute IsJobGroupPaused = Route(nameof(IsJobGroupPaused), Get, Jobs + "/groups/{jobGroup}/paused");

    // --- Triggers --------------------------------------------------------------------------------------

    public static readonly WireRoute QueryTriggers = Route(nameof(QueryTriggers), Get, Triggers);
    public static readonly WireRoute FetchTriggers = Route(nameof(FetchTriggers), Post, Triggers + "/fetch");
    public static readonly WireRoute GetTrigger = Route(nameof(GetTrigger), Get, Trigger);
    public static readonly WireRoute CheckTriggerExists = Route(nameof(CheckTriggerExists), Get, Trigger + "/exists");
    public static readonly WireRoute GetTriggerState = Route(nameof(GetTriggerState), Get, Trigger + "/state");
    public static readonly WireRoute ResetTriggerFromErrorState = Route(nameof(ResetTriggerFromErrorState), Post, Trigger + "/reset-from-error-state");
    public static readonly WireRoute ResetTriggerKeysFromErrorState = Route(nameof(ResetTriggerKeysFromErrorState), Post, Triggers + "/keys/reset-from-error-state");
    public static readonly WireRoute PauseTrigger = Route(nameof(PauseTrigger), Post, Trigger + "/pause");
    public static readonly WireRoute PauseTriggers = Route(nameof(PauseTriggers), Post, Triggers + "/pause");
    public static readonly WireRoute PauseTriggerKeys = Route(nameof(PauseTriggerKeys), Post, Triggers + "/keys/pause");
    public static readonly WireRoute ResumeTrigger = Route(nameof(ResumeTrigger), Post, Trigger + "/resume");
    public static readonly WireRoute ResumeTriggers = Route(nameof(ResumeTriggers), Post, Triggers + "/resume");
    public static readonly WireRoute ResumeTriggerKeys = Route(nameof(ResumeTriggerKeys), Post, Triggers + "/keys/resume");
    public static readonly WireRoute QueryTriggerGroups = Route(nameof(QueryTriggerGroups), Get, Triggers + "/groups");
    public static readonly WireRoute IsTriggerGroupPaused = Route(nameof(IsTriggerGroupPaused), Get, Triggers + "/groups/{triggerGroup}/paused");
    public static readonly WireRoute ScheduleJob = Route(nameof(ScheduleJob), Post, Triggers + "/schedule");
    public static readonly WireRoute ScheduleJobs = Route(nameof(ScheduleJobs), Post, Triggers + "/schedule-multiple");
    public static readonly WireRoute UnscheduleJob = Route(nameof(UnscheduleJob), Post, Trigger + "/unschedule");
    public static readonly WireRoute UnscheduleJobs = Route(nameof(UnscheduleJobs), Post, Triggers + "/unschedule");
    public static readonly WireRoute UnscheduleJobsByGroup = Route(nameof(UnscheduleJobsByGroup), Post, Triggers + "/unschedule-by-group");
    public static readonly WireRoute RescheduleJob = Route(nameof(RescheduleJob), Post, Trigger + "/reschedule");
    public static readonly WireRoute UpdateTriggerDetails = Route(nameof(UpdateTriggerDetails), Post, Trigger + "/update-details");
    public static readonly WireRoute BackfillTrigger = Route(nameof(BackfillTrigger), Post, Trigger + "/backfill");

    // --- Calendars -------------------------------------------------------------------------------------

    public static readonly WireRoute QueryCalendarNames = Route(nameof(QueryCalendarNames), Get, Calendars);
    public static readonly WireRoute GetCalendar = Route(nameof(GetCalendar), Get, Calendar);
    public static readonly WireRoute CheckCalendarExists = Route(nameof(CheckCalendarExists), Get, Calendar + "/exists");
    public static readonly WireRoute AddCalendar = Route(nameof(AddCalendar), Post, Calendars);
    public static readonly WireRoute DeleteCalendar = Route(nameof(DeleteCalendar), Delete, Calendar);

    /// <summary>
    /// Every route, in the order the table declares them.
    /// </summary>
    public static readonly ReadOnlyCollection<WireRoute> All = routes.AsReadOnly();

    /// <summary>
    /// The route a method and a path are a call of, with the values its parameters took, or
    /// <see langword="null" /> when the table has no such route.
    /// </summary>
    /// <remarks>
    /// No path is a call of two routes — <c>SchedulerRoutesTest</c> holds the table to that — so the
    /// first route that matches is the one ASP.NET Core would pick, and no precedence has to be decided.
    /// </remarks>
    /// <param name="method">The HTTP method, in any case.</param>
    /// <param name="path">The path relative to the API's root, with or without its query string.</param>
    public static (WireRoute Route, Dictionary<string, string> Values)? Match(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        foreach (WireRoute route in routes)
        {
            if (string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase)
                && route.TryMatch(path, out Dictionary<string, string>? values))
            {
                return (route, values);
            }
        }

        return null;
    }

    private static WireRoute Route(string name, string method, string template)
    {
        WireRoute route = new(name, method, template);
        routes.Add(route);
        return route;
    }
}
