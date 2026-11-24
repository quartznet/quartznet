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

using Quartz.AspNetCore;
using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.Mcp.Impl;

/// <summary>
/// Where what an agent did through MCP is written: the <c>9500</c> lines, and every
/// <see cref="IOperatorActionRecorder" /> the container holds — the dashboard's Action Log, in a process that maps
/// it — with <see cref="OperatorChannel.Mcp" />, the user and the client as it stated itself.
/// </summary>
/// <remarks>
/// A change and a change refused are entered; a read is logged when refused and otherwise not at all. The log line
/// comes first and is the record that has to exist; a recorder that throws is logged as the HTTP API logs one, and
/// never fails the call it describes.
/// </remarks>
internal sealed class McpRecords
{
    private readonly ILogger logger;
    private readonly IOperatorActionRecorder[] recorders;

    public McpRecords(ILoggerFactory loggerFactory, IEnumerable<IOperatorActionRecorder>? recorders = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        logger = loggerFactory.CreateLogger(McpLog.Category);
        this.recorders = recorders?.ToArray() ?? [];
    }

    /// <summary>
    /// A change was made, or answered as not done.
    /// </summary>
    public void Changed(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);

        string key = call.Key?.Key ?? "(none)";
        string subject = call.Subject ?? key;
        logger.ToolChanged(call.Caller.Name, call.Caller.ClientOrUnknown, call.Tool.Name, key, subject);

        Notify(new OperatorAction(OperatorChannel.Mcp, call.Caller.Name, key, Operation(call.Tool), subject, Succeeded: call.Applied)
        {
            Client = call.Caller.Client,
            Message = call.Note,
        });
    }

    /// <summary>
    /// A call was refused before anything reached the scheduler.
    /// </summary>
    public void Refused(McpCaller caller, QuartzTool tool, string schedulerKey, string? subject, AccessRefusal refusal)
    {
        logger.ToolRefused(caller.Name, tool.Name, schedulerKey, refusal.Message);

        if (refusal.Action)
        {
            Notify(new OperatorAction(OperatorChannel.Mcp, caller.Name, schedulerKey, Operation(tool), subject ?? schedulerKey, Succeeded: false)
            {
                Refused = true,
                Client = caller.Client,
                Message = refusal.Message,
            });
        }
    }

    public void ConfirmationRefused(McpCaller caller, QuartzTool tool, string schedulerKey, string reason)
    {
        logger.ConfirmationRefused(caller.Name, tool.Name, schedulerKey, reason);
    }

    public void RateLimited(McpCaller caller, QuartzTool tool, string schedulerKey, int retryAfterSeconds)
    {
        logger.RateLimited(caller.Name, tool.Name, schedulerKey, retryAfterSeconds);
    }

    /// <summary>
    /// A target did not serve the call: the HTTP API's own events, <c>9010</c> to <c>9014</c>, in this category.
    /// </summary>
    public void TargetDidNotServe(TargetFaultException fault, QuartzTool tool, string schedulerKey)
    {
        string operation = Operation(tool);
        switch (fault.Kind)
        {
            case TargetFault.Unavailable:
                logger.TargetUnavailable(fault.Target, operation, schedulerKey, fault.Reason);
                break;
            case TargetFault.TimedOut:
                logger.TargetTimedOut(fault.Target, operation, schedulerKey, fault.Reason);
                break;
            case TargetFault.Refused:
                logger.TargetRefused(fault.Target, operation, schedulerKey, fault.Reason);
                break;
            default:
                logger.TargetFailed(fault.Target, operation, schedulerKey, fault.Kind, fault.Reason);
                break;
        }
    }

    /// <summary>
    /// What the Action Log calls the action: the name of the route the tool changes through, the word an HTTP API
    /// entry and the dashboard's own button use for the same change.
    /// </summary>
    internal static string Operation(QuartzTool tool) => tool.Routes[0].Name;

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
}
