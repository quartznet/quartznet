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

using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Mcp.Impl;

/// <summary>
/// Runs every Quartz tool call through the one order of steps: read the arguments, refuse what is not offered,
/// authorize, count a change, resolve the key, run, translate a target's fault, and record.
/// </summary>
/// <remarks>
/// <para>
/// The order is the HTTP API's: every refusal comes before the scheduler is looked up, so a refused caller cannot
/// tell a missing scheduler from a forbidden one; a change is counted against the rate limit only once it is
/// allowed, so a refused caller spends nothing; and a preview is neither counted nor recorded, because it changes
/// nothing.
/// </para>
/// <para>
/// Every failure is a tool result with <c>isError</c>, in the shape of <see cref="ErrorAnswer" />: a code, a
/// message, and for a target that did not serve the call, the fault class and whether a change may have been
/// applied. Nothing the endpoint did not decide itself is passed on: a target's fault is the fixed text of
/// <see cref="TargetFaults" />, which never names the target's address.
/// </para>
/// </remarks>
internal sealed class McpToolInvoker
{
    private readonly McpAccess access;
    private readonly ChangeRateLimiter rateLimiter;
    private readonly McpRecords records;
    private readonly TimeProvider timeProvider;
    private readonly IHttpContextAccessor? httpContextAccessor;

    public McpToolInvoker(
        McpAccess access,
        ChangeRateLimiter rateLimiter,
        McpRecords records,
        TimeProvider timeProvider,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        this.access = access;
        this.rateLimiter = rateLimiter;
        this.records = records;
        this.timeProvider = timeProvider;
        this.httpContextAccessor = httpContextAccessor;
    }

    public async ValueTask<CallToolResult> Invoke(QuartzTool tool, RequestContext<CallToolRequestParams> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        McpCaller caller = McpCaller.Of(request, httpContextAccessor?.HttpContext);

        McpArguments? arguments = tool.ReadArguments(request.Params?.Arguments, out string? problem);
        if (arguments is null || (problem ??= arguments.Problem()) is not null)
        {
            return Error(new McpError(McpErrorCodes.InvalidArguments, problem!), scheduler: null);
        }

        bool preview = tool.Confirms && arguments is IConfirmedArguments { ConfirmationToken: null };

        if (!tool.NamesScheduler)
        {
            if (access.Unoffered(caller, tool) is { } unoffered)
            {
                records.Refused(caller, tool, "(none)", subject: null, unoffered);
                return Error(new McpError(unoffered.Code, unoffered.Message), scheduler: null);
            }

            return await Run(new ToolCall { Tool = tool, Caller = caller, Arguments = arguments, Services = services, Clock = timeProvider }, cancellationToken)
                .ConfigureAwait(false);
        }

        ISchedulerRepository repository = services.GetRequiredService<ISchedulerRepository>();
        SchedulerRef key = KeyOf(services, repository, arguments.NamedScheduler()!);
        string? subject = SubjectOf(arguments);

        if (await access.Refusal(caller, services, tool, key, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            // A refused change counts against the caller's minute, so a caller looping on one cannot fill the Action
            // Log or the log: past the limit it is still refused, and no longer recorded, and the limit is logged once.
            if (tool.Changes && !rateLimiter.TryAcquire(caller.Digest, out int refusedRetryAfter, out bool reportRefused))
            {
                if (reportRefused)
                {
                    records.RateLimited(caller, tool, key.Key, refusedRetryAfter);
                }
            }
            else
            {
                records.Refused(caller, tool, key.Key, subject, refusal);
            }

            return Error(new McpError(refusal.Code, refusal.Message), key.Key);
        }

        if (tool.Changes && !preview && !rateLimiter.TryAcquire(caller.Digest, out int retryAfter, out bool report))
        {
            if (report)
            {
                records.RateLimited(caller, tool, key.Key, retryAfter);
            }

            return Error(
                new McpError(
                    McpErrorCodes.RateLimited,
                    $"Too many changes: this endpoint allows {services.GetRequiredService<Microsoft.Extensions.Options.IOptions<QuartzMcpOptions>>().Value.MaxChangesPerMinute} a minute per caller. Try again in {retryAfter} s.")
                {
                    RetryAfterSeconds = retryAfter
                },
                key.Key);
        }

        SchedulerResolution? resolution = key.Target is null
            ? SchedulerLookup.Resolve(repository, services.GetService<SchedulerWindowRegistry>(), key)
            : SchedulerLookup.Targeted(repository, services.GetService<SchedulerWindowRegistry>(), key);
        if (resolution is null)
        {
            return Error(new McpError(McpErrorCodes.NotFound, SchedulerLookup.NotFoundMessage(repository, key)), key.Key);
        }

        ToolCall call = new()
        {
            Tool = tool,
            Caller = caller,
            Arguments = arguments,
            Services = services,
            Clock = timeProvider,
            Key = resolution.Ref,
            Resolution = resolution,
            IsPreview = preview,
            Subject = subject,
        };

        return await Run(call, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CallToolResult> Run(ToolCall call, CancellationToken cancellationToken)
    {
        string? schedulerKey = call.Key?.Key;
        JsonElement answer;
        try
        {
            answer = await call.Tool.Run(call, cancellationToken).ConfigureAwait(false);
        }
        catch (McpToolException exception)
        {
            return Error(new McpError(exception.Code, Truncate(exception.Message)), schedulerKey);
        }
        catch (Exception exception) when (Translated(call, exception, cancellationToken) is { } error)
        {
            return Error(error, schedulerKey);
        }

        if (call.Tool.Changes && !call.IsPreview)
        {
            records.Changed(call);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = answer.GetRawText() }],
            StructuredContent = answer,
            IsError = false,
        };
    }

    /// <summary>
    /// What a failure inside a tool is answered as, or <see langword="null" /> for one to let the SDK answer, which
    /// it does with a message that says nothing of the failure.
    /// </summary>
    private McpError? Translated(ToolCall call, Exception exception, CancellationToken cancellationToken)
    {
        string? schedulerKey = call.Key?.Key;

        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (call.Key?.Target is not null && call.Resolution?.Scheduler is IProxyScheduler
            && TargetFaults.Translate(exception, call.Key, call.Tool.Changes, cancellationToken) is { } translated)
        {
            if (translated is TargetFaultException fault)
            {
                records.TargetDidNotServe(fault, call.Tool, schedulerKey!);
                return new McpError(McpErrorCodes.TargetFault, fault.Message)
                {
                    Fault = FaultName(fault.Kind),
                    MayHaveApplied = fault.MayHaveApplied ? true : null,
                };
            }

            return new McpError(McpErrorCodes.NotServed, Truncate(translated.Message));
        }

        return exception switch
        {
            InvalidRequestException invalid => new McpError(McpErrorCodes.InvalidArguments, Truncate(invalid.Message)),
            NotSupportedException notServed => new McpError(McpErrorCodes.NotServed, Truncate(notServed.Message)),
            SchedulerException scheduler => new McpError(McpErrorCodes.SchedulerError, Truncate(scheduler.Message)),
            _ => null,
        };
    }

    private static string FaultName(TargetFault kind) => kind switch
    {
        TargetFault.Unavailable => "unavailable",
        TargetFault.TimedOut => "timedOut",
        TargetFault.Refused => "refused",
        _ => "failed",
    };

    private CallToolResult Error(McpError error, string? scheduler)
    {
        ErrorAnswer answer = new(error) { AsOfUtc = timeProvider.GetUtcNow(), Scheduler = scheduler };
        JsonElement element = JsonSerializer.SerializeToElement(answer, McpJsonContext.Default.ErrorAnswer);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = error.Message }],
            StructuredContent = element,
            IsError = true,
        };
    }

    /// <summary>
    /// The key a call names, read as the repository resolves it, then as every policy is asked about it by
    /// <see cref="SchedulerLookup.PolicyKey" />: the target in its own spelling, and a window named by its bare name
    /// behind its store's target.
    /// </summary>
    internal static SchedulerRef KeyOf(IServiceProvider services, ISchedulerRepository repository, string key)
    {
        return SchedulerLookup.PolicyKey(services, SchedulerLookup.Read(repository, key.Trim()));
    }

    /// <summary>
    /// What a change is made to, as the HTTP API's Action Log entry names it: <c>group.name</c>, or a firing.
    /// </summary>
    private static string? SubjectOf(McpArguments arguments) => arguments switch
    {
        JobArguments job => job.Group + "." + job.Name,
        TriggerArguments trigger => trigger.Group + "." + trigger.Name,
        PauseArguments pause => pause.Group + "." + pause.Name,
        RescheduleArguments reschedule => reschedule.Group + "." + reschedule.Name,
        UnscheduleArguments unschedule => unschedule.Group + "." + unschedule.Name,
        InterruptArguments interrupt => interrupt.FireInstanceId,
        _ => null,
    };

    internal static string Truncate(string message)
    {
        return message.Length <= McpLimits.MaxErrorMessageLength ? message : message[..McpLimits.MaxErrorMessageLength] + "…";
    }
}
