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

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

using static Quartz.Trimming.Canary.Wire.CanarySteps;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The dashboard agent's wire out of a trimmed or natively compiled publish: a second scheduler, in a
/// container of its own, dials <see cref="AgentCanaryHub" /> through <c>UseDashboardAgent</c> and is
/// driven down that connection the way a dashboard drives one.
/// </summary>
/// <remarks>
/// <para>
/// Each step is a round trip the dashboard makes: the registration the agent sends on connecting, the
/// heartbeat it sends on the interval the hub set, and then the HTTP API's own requests carried as
/// <see cref="AgentRequest" /> — a read, a schedule naming a job type as a string, a trigger with the
/// event stream open, a refusal the agent decides itself, a <c>404</c> with problem details — each
/// answered as <see cref="AgentAnswer" /> with the body the API would have written, read back here through
/// the contract's generated metadata. Last, the agent's goodbye when its scheduler shuts down.
/// </para>
/// <para>
/// Everything the agent does here it does with reflection-based System.Text.Json switched off: the hub
/// payloads through <c>HubConnection.On&lt;T&gt;</c>, <c>InvokeAsync&lt;T&gt;</c> and a client-to-server
/// stream, and the bodies inside them through the wire format. A compile cannot stand in for any of it —
/// the SignalR client reads the hub protocol's payloads at run time, and the agent's own
/// <c>AgentCarrier</c> resolves a job type from a name.
/// </para>
/// </remarks>
internal static class AgentCheck
{
    /// <summary>
    /// The agent's scheduler, which is not the one the HTTP API serves: an agent is a scheduler reached
    /// through no inbound port, and this one has none.
    /// </summary>
    internal const string SchedulerName = "AgentCanary";

    /// <summary>
    /// The name the agent registers under: the first half of its key on a dashboard.
    /// </summary>
    internal const string Target = "canary";

    private static readonly JobKey JobKey = new("agent", "canary");
    private static readonly TriggerKey TriggerKey = new("agent", "canary");

    /// <summary>
    /// Noon on the first of January 2099, so the trigger fires only when the hub says so.
    /// </summary>
    private const string Cron = "0 0 12 1 1 ? 2099";

    private static readonly DateTimeOffset FirstFireTime = new(2099, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Payload = "the job data map crosses the tunnel in both directions";

    private const string FiredBy = "AgentCanaryHub";

    /// <summary>
    /// How long the hub gives the agent for one request, as a dashboard's <c>OperationTimeout</c> does.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>
    /// The wire format the bodies inside a request and an answer are written in, as the agent and the
    /// dashboard both have it: the contract's metadata and Quartz's converters, with reflection nowhere
    /// in the chain.
    /// </summary>
    private static readonly JsonSerializerOptions Wire = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

    /// <summary>
    /// Starts the agent's scheduler against the hub at <paramref name="hubUri" /> and runs every step.
    /// </summary>
    /// <param name="host">The Kestrel host's container, which holds the hub and what it has heard.</param>
    /// <param name="hubUri">Where <see cref="AgentCanaryHub" /> is mapped.</param>
    /// <param name="cancellationToken">Bounds the whole run.</param>
    public static async Task<bool> Run(IServiceProvider host, Uri hubUri, CancellationToken cancellationToken)
    {
        AgentCanaryHubState heard = host.GetRequiredService<AgentCanaryHubState>();
        Tunnel tunnel = new(host.GetRequiredService<IHubContext<AgentCanaryHub>>(), heard, cancellationToken);

        ServiceProvider agent = BuildAgent(hubUri);
        await using ConfiguredAsyncDisposable agentDisposal = agent.ConfigureAwait(false);

        IScheduler scheduler = await agent.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken).ConfigureAwait(false);
        await scheduler.Start(cancellationToken).ConfigureAwait(false);

        (string Name, Func<Task<string>> Step)[] steps =
        [
            ("register", () => Register(heard, tunnel, cancellationToken)),
            ("heartbeat", () => Heartbeat(heard, cancellationToken)),
            ("details", () => Details(tunnel)),
            ("schedule", () => Schedule(tunnel)),
            ("read-back", () => ReadBack(tunnel)),
            ("watch", () => Watch(heard, tunnel, cancellationToken)),
            ("trigger", () => Trigger(heard, tunnel, cancellationToken)),
            ("refused", () => Refused(tunnel)),
            ("not-found", () => NotFound(tunnel)),
            ("unregister", () => Unregister(scheduler, heard, cancellationToken)),
        ];

        return await CanarySteps.Run("agent", steps).ConfigureAwait(false);
    }

    /// <summary>
    /// The agent's container, as a worker's would be: a scheduler over the in-memory store, the job type
    /// a dashboard may name registered the way the trimming how-to says, and the agent plugin dialling
    /// out with the hub's token.
    /// </summary>
    private static ServiceProvider BuildAgent(Uri hubUri)
    {
        ServiceCollection services = new();

        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "canary-node";
            });

            // The request down the tunnel carries the type as a string, and this is what keeps the type
            // for the agent's scheduler to resolve it to.
            quartz.AddJobType<AgentCanaryJob>();

            quartz.UseDashboardAgent(options =>
            {
                options.Endpoint = hubUri;
                options.Token = AgentCanaryHub.Token;
                options.Target = Target;

                // An agent refuses every job type until told which it accepts, and every operation until
                // told which it refuses. Both decisions are the agent's and are made in its own process.
                options.IsJobTypeAllowed = static name => name.StartsWith("Quartz.Trimming.Canary.Wire.", StringComparison.Ordinal);
                options.IsOperationAllowed = static name => !string.Equals(name, SchedulerRoutes.Shutdown.Name, StringComparison.Ordinal);
            });
        });

        return services.BuildServiceProvider();
    }

    private static async Task<string> Register(AgentCanaryHubState heard, Tunnel tunnel, CancellationToken cancellationToken)
    {
        Task<(string ConnectionId, AgentRegistration Registration)> registered = heard.Registered;
        Task finished = await Task.WhenAny(registered, Task.Delay(Patience, Clock, cancellationToken)).ConfigureAwait(false);
        if (finished != registered)
        {
            throw new CanaryFailedException(heard.Refusal is { } refusal
                ? $"the hub refused the agent's connection for {refusal}, so it never registered."
                : $"the agent did not register within {PatienceSeconds} seconds.");
        }

        (string connectionId, AgentRegistration registration) = await registered.ConfigureAwait(false);
        tunnel.ConnectionId = connectionId;

        Expect(registration.ProtocolVersion == AgentProtocol.Version, $"the agent speaks protocol version {registration.ProtocolVersion}, not {AgentProtocol.Version}.");
        Expect(registration.Target == Target, $"the agent registered as target '{registration.Target}'.");
        Expect(registration.SchedulerName == SchedulerName, $"the agent registered the scheduler '{registration.SchedulerName}'.");
        Expect(!string.IsNullOrEmpty(registration.QuartzVersion), "the agent registered with no Quartz version.");
        Expect(!registration.ReadOnly, "the agent registered as read-only, which it was not configured to be.");

        foreach (WireRoute route in new[] { SchedulerRoutes.GetSchedulerDetails, SchedulerRoutes.ScheduleJob, SchedulerRoutes.TriggerJob, SchedulerRoutes.Shutdown })
        {
            Expect(registration.Routes.Contains(route.Name, StringComparer.Ordinal), $"the agent's registration does not offer {route.Name}, so a dashboard would never send it.");
        }

        return $"'{registration.Target}/{registration.SchedulerName}' on Quartz {registration.QuartzVersion}, offering {registration.Routes.Length} routes, over a bearer token in the Authorization header";
    }

    private static async Task<string> Heartbeat(AgentCanaryHubState heard, CancellationToken cancellationToken)
    {
        AgentHeartbeat heartbeat = await Await(heard.FirstHeartbeat,
            $"no heartbeat arrived within {PatienceSeconds} seconds, though the hub asked for one every {AgentCanaryHubState.HeartbeatInterval.TotalMilliseconds} ms.",
            cancellationToken).ConfigureAwait(false);

        Expect(heartbeat.Status == SchedulerStatus.Running, $"the heartbeat says the scheduler is {heartbeat.Status}, not running.");
        Expect(heartbeat.SentAtUtc > DateTimeOffset.MinValue, "the heartbeat carries no timestamp.");

        return $"the scheduler is {heartbeat.Status}, on the interval the hub set";
    }

    private static async Task<string> Details(Tunnel tunnel)
    {
        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.GetSchedulerDetails.For(SchedulerName)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.OK, $"the details request was answered {answer.Status}: {Text(answer)}");

        SchedulerDto details = Read<SchedulerDto>(answer);
        Expect(details.Name == SchedulerName, $"the agent answered for the scheduler '{details.Name}'.");
        Expect(details.Status == SchedulerStatus.Running, $"the scheduler is {details.Status}, not running.");

        return $"'{details.Name}' is {details.Status} on {details.JobStore.Type}, read as a SchedulerDto out of the answer's body";
    }

    private static async Task<string> Schedule(Tunnel tunnel)
    {
        IJobDetail job = JobBuilder.Create<AgentCanaryJob>()
            .WithIdentity(JobKey)
            .UsingJobData(WireCanaryRun.PayloadKey, Payload)
            .Build();

        ITrigger trigger = TriggerBuilder.Create(Clock)
            .WithIdentity(TriggerKey)
            .WithSchedule(CronScheduleBuilder.Create(Cron).InTimeZone(TimeZoneInfo.Utc))
            .Build();

        // The body the dashboard's HttpScheduler writes for the same call: the job as a DTO naming its
        // type as a string, which the agent's IsJobTypeAllowed admits and its carrier resolves.
        ScheduleJobRequest request = new(trigger, JobDetailDto.Create(job));

        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.ScheduleJob.For(SchedulerName), Write(request)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.OK, $"the schedule request was answered {answer.Status}: {Text(answer)}");

        ScheduleJobResponse response = Read<ScheduleJobResponse>(answer);
        Expect(response.FirstFireTimeUtc == FirstFireTime, $"the agent says the trigger first fires at {response.FirstFireTimeUtc:O}, not {FirstFireTime:O}.");

        return $"{JobKey} as {request.Job!.JobType} with a cron trigger, first firing at {response.FirstFireTimeUtc:O}";
    }

    private static async Task<string> ReadBack(Tunnel tunnel)
    {
        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.GetJobDetails.For(SchedulerName, JobKey.Group, JobKey.Name)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.OK, $"the job could not be read back: {answer.Status}: {Text(answer)}");

        JobDetailDto job = Read<JobDetailDto>(answer);
        string jobType = new JobType(typeof(AgentCanaryJob)).FullName;
        Expect(job.JobType == jobType, $"the job came back as '{job.JobType}', not '{jobType}'.");
        Expect(job.JobDataMap.GetString(WireCanaryRun.PayloadKey) == Payload, $"the job data came back as '{job.JobDataMap.GetString(WireCanaryRun.PayloadKey)}'.");

        return $"the job as {job.JobType} with its data";
    }

    /// <summary>
    /// Tells the agent the dashboard is watching, which opens its client-to-server event stream, and
    /// waits until that stream is live: a pause and a resume through the tunnel until the resume's event
    /// comes back up it. The stream is opened by the agent on its own time, so a trigger fired before it
    /// was would raise an event nobody heard.
    /// </summary>
    private static async Task<string> Watch(AgentCanaryHubState heard, Tunnel tunnel, CancellationToken cancellationToken)
    {
        await tunnel.Send(AgentProtocol.Watch, true).ConfigureAwait(false);

        long started = Clock.GetTimestamp();
        int probes = 0;
        while (!heard.Events.Any(static e => e.Kind == SchedulerEventKind.TriggerResumed))
        {
            Expect(Clock.GetElapsedTime(started) < Patience, $"no event came up the tunnel within {PatienceSeconds} seconds of the agent being told to watch, after {probes} pause-and-resume probes.");

            probes++;
            AgentAnswer paused = await tunnel.Execute(SchedulerRoutes.PauseTrigger.For(SchedulerName, TriggerKey.Group, TriggerKey.Name)).ConfigureAwait(false);
            Expect(paused.Status == (int) HttpStatusCode.OK, $"the probe's pause was answered {paused.Status}: {Text(paused)}");

            AgentAnswer resumed = await tunnel.Execute(SchedulerRoutes.ResumeTrigger.For(SchedulerName, TriggerKey.Group, TriggerKey.Name)).ConfigureAwait(false);
            Expect(resumed.Status == (int) HttpStatusCode.OK, $"the probe's resume was answered {resumed.Status}: {Text(resumed)}");

            await Task.Delay(TimeSpan.FromMilliseconds(100), Clock, cancellationToken).ConfigureAwait(false);
        }

        SchedulerEvent first = heard.Events.First(static e => e.Kind == SchedulerEventKind.TriggerResumed);
        Expect(first.SchedulerName == SchedulerName, $"the event names the scheduler '{first.SchedulerName}'.");
        Expect(first.TriggerKey is { } key && key.Name == TriggerKey.Name && key.Group == TriggerKey.Group, $"the event is about the trigger '{first.TriggerKey?.Group}.{first.TriggerKey?.Name}'.");

        return $"the agent streams its events up the tunnel: a {first.Kind} for {TriggerKey} arrived after {probes} probe(s)";
    }

    private static async Task<string> Trigger(AgentCanaryHubState heard, Tunnel tunnel, CancellationToken cancellationToken)
    {
        TriggerJobRequest request = new(new JobDataMap { { WireCanaryRun.FiredByKey, FiredBy } });

        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.TriggerJob.For(SchedulerName, JobKey.Group, JobKey.Name), Write(request)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.OK, $"the trigger request was answered {answer.Status}: {Text(answer)}");

        WireCanaryRun run = await Await(AgentCanaryJob.Ran.Task,
            $"the job never ran within {PatienceSeconds} seconds, so the agent's scheduler could not resolve or build the type the request named.",
            cancellationToken).ConfigureAwait(false);

        Expect(run.Payload == Payload, $"the job ran with the payload '{run.Payload}'.");
        Expect(run.FiredBy == FiredBy, $"the job ran with fired-by '{run.FiredBy}'.");

        SchedulerEvent executed = await Await(heard.JobExecuted,
            $"the job ran, and no JobExecuted event came up the tunnel within {PatienceSeconds} seconds.",
            cancellationToken).ConfigureAwait(false);

        Expect(executed.SchedulerName == SchedulerName, $"the event names the scheduler '{executed.SchedulerName}'.");
        Expect(executed.JobKey is { } key && key.Name == JobKey.Name && key.Group == JobKey.Group, $"the event is about the job '{executed.JobKey?.Group}.{executed.JobKey?.Name}'.");
        Expect(executed.Vetoed != true, "the event says the job was vetoed.");
        Expect(executed.ExceptionMessage is null, $"the event says the job threw: {executed.ExceptionMessage}");
        Expect(!string.IsNullOrEmpty(executed.FireInstanceId), "the event names no firing.");

        await tunnel.Send(AgentProtocol.Watch, false).ConfigureAwait(false);

        return $"{JobKey} ran in the agent's scheduler with the data it was scheduled with and the data it was triggered with, and its {executed.Kind} came up the tunnel";
    }

    private static async Task<string> Refused(Tunnel tunnel)
    {
        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.Shutdown.For(SchedulerName)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.Forbidden, $"a shutdown the agent was configured to refuse was answered {answer.Status}: {Text(answer)}");

        ProblemDetailsDto problem = Read<ProblemDetailsDto>(answer);
        Expect(problem.Status == (int) HttpStatusCode.Forbidden, $"the problem details say status {problem.Status}.");
        Expect(problem.Detail?.Contains(SchedulerRoutes.Shutdown.Name, StringComparison.Ordinal) == true, $"the problem details do not name the refused operation: '{problem.Detail}'.");

        return $"{SchedulerRoutes.Shutdown.Name} is answered 403 with problem details saying so: '{problem.Detail}'";
    }

    private static async Task<string> NotFound(Tunnel tunnel)
    {
        JobKey absent = new("absent", JobKey.Group);

        AgentAnswer answer = await tunnel.Execute(SchedulerRoutes.GetJobDetails.For(SchedulerName, absent.Group, absent.Name)).ConfigureAwait(false);
        Expect(answer.Status == (int) HttpStatusCode.NotFound, $"a job that was never scheduled was answered {answer.Status}: {Text(answer)}");

        ProblemDetailsDto problem = Read<ProblemDetailsDto>(answer);
        Expect(problem.Status == (int) HttpStatusCode.NotFound, $"the problem details say status {problem.Status}.");
        Expect(problem.Detail?.Contains(absent.Name, StringComparison.Ordinal) == true, $"the problem details do not name the job: '{problem.Detail}'.");

        return $"{absent} is answered 404 with problem details naming it";
    }

    private static async Task<string> Unregister(IScheduler scheduler, AgentCanaryHubState heard, CancellationToken cancellationToken)
    {
        await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken).ConfigureAwait(false);

        await Await(heard.Unregistered,
            $"the agent's scheduler shut down, and no {AgentProtocol.Unregister} reached the hub within {PatienceSeconds} seconds.",
            cancellationToken).ConfigureAwait(false);

        return $"the agent said {AgentProtocol.Unregister} as its scheduler shut down";
    }

    // --- The bodies -----------------------------------------------------------------------------------

    private static byte[] Write<T>(T body) => JsonSerializer.SerializeToUtf8Bytes(body, Format<T>());

    private static T Read<T>(AgentAnswer answer)
    {
        return JsonSerializer.Deserialize(answer.Body, Format<T>())
            ?? throw new CanaryFailedException($"the answer's body read back as null rather than as a {typeof(T).Name}: {Text(answer)}");
    }

    private static JsonTypeInfo<T> Format<T>() => (JsonTypeInfo<T>) Wire.GetTypeInfo(typeof(T));

    private static string Text(AgentAnswer answer) => answer.Body.Length == 0 ? "(no body)" : System.Text.Encoding.UTF8.GetString(answer.Body);

    /// <summary>
    /// The hub's side of a request: sends an <see cref="AgentProtocol.Execute" /> to the agent's connection
    /// and waits for the <see cref="AgentProtocol.Answer" /> that names its id, as the dashboard's
    /// <c>AgentWireTransport</c> does.
    /// </summary>
    private sealed class Tunnel(IHubContext<AgentCanaryHub> hub, AgentCanaryHubState heard, CancellationToken cancellationToken)
    {
        private int nextId;

        /// <summary>The agent's connection, known once it has registered.</summary>
        public string? ConnectionId { get; set; }

        public Task<AgentAnswer> Execute(WireRequest request, byte[]? body = null)
        {
            string id = Interlocked.Increment(ref nextId).ToString(CultureInfo.InvariantCulture);

            AgentRequest message = new()
            {
                Id = id,
                Method = request.Route.Method,
                Path = request.Path,
                Body = body,
                Timeout = RequestTimeout,
            };

            Task<AgentAnswer> answer = heard.AnswerTo(id);
            return Run(message, answer);
        }

        private async Task<AgentAnswer> Run(AgentRequest message, Task<AgentAnswer> answer)
        {
            await Send(AgentProtocol.Execute, message).ConfigureAwait(false);

            return await Await(answer,
                $"{message.Method} {message.Path} was not answered within {PatienceSeconds} seconds.",
                cancellationToken).ConfigureAwait(false);
        }

        public Task Send(string method, object argument)
        {
            Expect(ConnectionId is not null, "the agent has not registered, so there is no connection to send to.");
            return hub.Clients.Client(ConnectionId).SendAsync(method, argument, cancellationToken);
        }
    }
}
