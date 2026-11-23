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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Writes the line that says a request changed something, and who asked for it.
/// </summary>
/// <remarks>
/// <para>
/// Its own type rather than a member of <see cref="ExceptionHandler" />, which is where everything else
/// the API logs about a request is written from: a mutation that succeeded is not an exception, and a
/// handler named for them would be the wrong place to look for it. Both write under
/// <see cref="HttpApiLog.Category" />, so an operator still filters the API by one name.
/// </para>
/// <para>
/// A singleton, because nothing here is a request's: what varies per request is read off the
/// <see cref="HttpContext" /> it is handed. The alternative — resolving <see cref="ILoggerFactory" />
/// per request and asking it for the category — takes a lock inside the factory on a path that runs for
/// every mutation.
/// </para>
/// </remarks>
internal sealed class MutationAudit
{
    private readonly ILogger logger;
    private readonly IOperatorActionRecorder[] recorders;

    /// <param name="loggerFactory">Where the audit lines go.</param>
    /// <param name="recorders">
    /// Where else an action is recorded: the dashboard's Action Log, in a process that maps it. None in a
    /// process that does not, which records the log line alone.
    /// </param>
    public MutationAudit(ILoggerFactory loggerFactory, IEnumerable<IOperatorActionRecorder>? recorders = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        logger = loggerFactory.CreateLogger(HttpApiLog.Category);
        this.recorders = recorders?.ToArray() ?? [];
    }

    /// <summary>
    /// Records that <paramref name="context" />'s request changed something.
    /// </summary>
    /// <remarks>
    /// Called once per successful mutating request, from the wrapper that already decides what a mutating
    /// request is — so a route added later is audited by carrying the same marker
    /// <see cref="QuartzHttpApiOptions.ReadOnly" /> refuses it by, and by nothing else.
    /// </remarks>
    public void Record(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string user = UserName(context);
        string operation = Operation(context);
        string schedulerKey = SchedulerKey(context);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.MutationPerformed(user, operation, schedulerKey, context.Request.Path.ToString());
        }

        Notify(new OperatorAction(OperatorChannel.HttpApi, user, schedulerKey, operation, Subject(context, schedulerKey), Succeeded: true));
    }

    /// <summary>
    /// Records that <paramref name="context" />'s caller was refused <paramref name="operation" /> on
    /// <paramref name="schedulerName" /> by the operation policy, which said it needs <paramref name="access" />.
    /// </summary>
    /// <remarks>
    /// Here beside <see cref="Record" /> because it is the other half of the same trail: who did what, and
    /// who reached for what their role does not allow.
    /// </remarks>
    public void Refused(HttpContext context, string operation, string schedulerName, SchedulerAccess access)
    {
        ArgumentNullException.ThrowIfNull(context);

        string user = UserName(context);
        logger.OperationRefused(user, operation, schedulerName, access);

        Notify(new OperatorAction(OperatorChannel.HttpApi, user, schedulerName, operation, Subject(context, schedulerName), Succeeded: false)
        {
            Refused = true,
            Message = $"needs {access}"
        });
    }

    /// <summary>
    /// Hands the action to every recorder, none of which may fail the request it describes.
    /// </summary>
    /// <remarks>
    /// After the log line, which is the record that has to exist: a recorder that throws is a bug in the
    /// recorder, and the change it describes has already been made.
    /// </remarks>
    private void Notify(OperatorAction action)
    {
        foreach (IOperatorActionRecorder recorder in recorders)
        {
            try
            {
                recorder.Record(action);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger.RecorderFailed(recorder.GetType().Name, exception);
            }
        }
    }

    /// <summary>
    /// Who made the request, or <c>(anonymous)</c> where nothing authenticated.
    /// </summary>
    /// <remarks>
    /// The name the dashboard's own action log uses for the same absence. An API mapped with
    /// <c>AllowAnonymous()</c> writes it on every line, which is the record such a deployment has: that
    /// somebody who could reach the endpoint did this.
    /// </remarks>
    private static string UserName(HttpContext context)
    {
        string? name = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "(anonymous)" : name;
    }

    /// <summary>
    /// What was done, as the endpoint's own name — <c>PauseTrigger</c>, <c>Shutdown</c>.
    /// </summary>
    /// <remarks>
    /// The name <c>WithName</c> gave the route rather than the path, because it is the same word in every
    /// deployment: the path carries whatever <see cref="QuartzHttpApiOptions.ApiPath" /> is and the keys
    /// of the request's target, so an operator matching on what was done would be matching on those too.
    /// </remarks>
    private static string Operation(HttpContext context)
    {
        Endpoint? endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
               ?? endpoint?.DisplayName
               ?? "(unknown)";
    }

    /// <summary>
    /// Which scheduler the request was for: the route's name, and from 4.9 the query's target as the first
    /// half of the key — <c>w1/QuartzScheduler</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every mutating route the API maps names one — that is what makes the per-scheduler policy a
    /// convention over the route pattern rather than a call in each handler — so the fallback is for a
    /// route of somebody else's that carries the marker without naming a scheduler.
    /// </para>
    /// <para>
    /// The key rather than the name, because the route the line also carries is the path, and the target is
    /// in the query, which the path leaves out. The template is unchanged; a bare request logs the bare name
    /// it always did.
    /// </para>
    /// </remarks>
    private static string SchedulerKey(HttpContext context)
    {
        return context.Request.RouteValues[SchedulerAuthorization.SchedulerNameRouteValue] is string schedulerName
            ? TargetedRequest.Key(context, schedulerName).Key
            : "(none)";
    }

    /// <summary>
    /// What the action was done to, as the route names it: a job or trigger as <c>group.name</c>, a
    /// group, a calendar, a firing — or the scheduler itself.
    /// </summary>
    private static string Subject(HttpContext context, string schedulerKey)
    {
        RouteValueDictionary values = context.Request.RouteValues;

        if (values["jobGroup"] is string jobGroup)
        {
            return values["jobName"] is string jobName ? jobGroup + "." + jobName : jobGroup;
        }

        if (values["triggerGroup"] is string triggerGroup)
        {
            return values["triggerName"] is string triggerName ? triggerGroup + "." + triggerName : triggerGroup;
        }

        if (values["calendarName"] is string calendarName)
        {
            return calendarName;
        }

        if (values["fireInstanceId"] is string fireInstanceId)
        {
            return fireInstanceId;
        }

        return schedulerKey;
    }
}
