using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.HttpApiContract;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class EndpointHelper
{
    private readonly IOptions<JsonOptions> jsonOptions;
    private readonly int maxPageSize;

    /// <summary>
    /// Takes the application's HTTP JSON options, which <see cref="QuartzJsonOptionsSetup" /> has taught
    /// the wire contract by the time they are read. The endpoints already receive this type as a
    /// parameter, so writing a response through it costs no registration and no extra lookup — which is
    /// also why <see cref="QuartzHttpApiOptions.MaxPageSize" /> is read here rather than at each of the
    /// six listing endpoints.
    /// </summary>
    public EndpointHelper(IOptions<JsonOptions> jsonOptions, IOptions<QuartzHttpApiOptions> apiOptions)
    {
        this.jsonOptions = jsonOptions;
        maxPageSize = apiOptions.Value.MaxPageSize;
        IsJobTypeAllowed = apiOptions.Value.IsJobTypeAllowed;
    }

    /// <summary>
    /// <see cref="QuartzHttpApiOptions.IsJobTypeAllowed" />, read once and handed to
    /// <see cref="RequestedJobDetail.From" /> by the three endpoints that take a job in their body.
    /// </summary>
    /// <remarks>
    /// Read here for the reason <see cref="QuartzHttpApiOptions.MaxPageSize" /> is: the endpoints already
    /// receive this type, so an option one of them needs costs no second injection.
    /// </remarks>
    public Func<string, bool>? IsJobTypeAllowed { get; }

    /// <summary>
    /// The one place the API turns a response into JSON. Generic because every caller already has the
    /// static type in hand: erasing it to <see cref="object" /> binds the overload that has to rediscover
    /// the type at runtime, where this one carries it through to the serializer.
    /// </summary>
    /// <remarks>
    /// The metadata is asked for rather than left to be discovered. <c>HttpApiJsonContext</c> states every
    /// body this API returns and sits in front of whatever resolver the options already had, so
    /// <see cref="JsonSerializerOptions.GetTypeInfo" /> answers from generated metadata — and passing the
    /// <see cref="JsonTypeInfo{T}" /> binds the <see cref="Results.Json{TValue}(TValue, JsonTypeInfo{TValue}, string, int?)" />
    /// overload that carries neither <c>RequiresUnreferencedCode</c> nor <c>RequiresDynamicCode</c>. The
    /// open half of the contract is unaffected: metadata generated for a type the options carry a
    /// converter for defers to that converter, so an <see cref="ITrigger" /> or an <see cref="ICalendar" />
    /// still goes out through Quartz's own.
    /// </remarks>
    public IResult JsonResponse<T>(T data) where T : notnull
    {
        JsonSerializerOptions serializerOptions = jsonOptions.Value.SerializerOptions;
        return Results.Json(data, (JsonTypeInfo<T>) serializerOptions.GetTypeInfo(typeof(T)));
    }

    /// <summary>
    /// The most keys one bulk fetch request may carry.
    /// </summary>
    public const int MaxKeysToFetch = 1000;

    /// <summary>
    /// What the generated OpenAPI document says about a listing's <c>take</c>, carried by a
    /// <see cref="System.ComponentModel.DescriptionAttribute" /> on each of the six parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parameter is a <see langword="string" /> because <c>?take=all</c> is one of the two things it
    /// accepts, and a document that says <c>string</c> where a reader expects <c>integer</c> looks like a
    /// mistake unless something says why. This is that something ([#3682](https://github.com/quartznet/quartznet/issues/3682)).
    /// </para>
    /// <para>
    /// A description rather than a schema of its own. The honest schema is
    /// <c>oneOf: [integer, const "all"]</c>, and no OpenAPI generator infers it from an
    /// <see cref="IParsable{TSelf}" /> — every one of them would still render the CLR type, so getting it
    /// would take a schema filter written once per generator the host might be using. Quartz generates no
    /// document of its own; the host's generator does, and there are several. A description is what all
    /// of them read, so it is what this says.
    /// </para>
    /// </remarks>
    public const string TakeDescription = "A page size, or \"" + HttpApiConstants.AllItems + "\" for everything up to MaxPageSize";

    /// <summary>
    /// Reads the paging a listing request carried — and a fire-instance listing's state — with this API's
    /// <see cref="QuartzHttpApiOptions.MaxPageSize" />, before any scheduler is looked up.
    /// </summary>
    /// <remarks>
    /// <see cref="ListingParameters.Read" /> says what <c>take</c> and <c>state</c> accept and why. A
    /// listing's matchers are set on the result and read by the operation that applies them.
    /// </remarks>
    /// <exception cref="InvalidRequestException">The page or the state is malformed.</exception>
    public ListingParameters Listing(int skip, string? take, bool includeTotalCount, string? state = null)
    {
        return ListingParameters.Read(skip, take, includeTotalCount, maxPageSize, state);
    }

    public static void AssertKeysToFetch(KeyDto[] keys)
    {
        if (keys is null)
        {
            throw new BadHttpRequestException("Keys to fetch are required");
        }

        if (keys.Length > MaxKeysToFetch)
        {
            throw new BadHttpRequestException($"Too many keys given, at most {MaxKeysToFetch} can be fetched at once");
        }

        foreach (KeyDto key in keys)
        {
            AssertIsValid(key);
        }
    }

    /// <summary>
    /// What a pause request's optional body says, with the authenticated user as the requester when
    /// the body names none — or <see langword="null" /> for no body at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user is the name <c>MutationAudit</c> logs the same request under. A body that names a
    /// requester of its own wins, which is how a dashboard calling on its operator's behalf through a
    /// service identity says whose pause it is; the audit line still says who called.
    /// </para>
    /// <para>
    /// No body is the pause every caller before 4.3 made, so it is answered as that pause was: through
    /// the reasonless member, recording nothing, and not in the user's name.
    /// </para>
    /// </remarks>
    public static PauseDetails? PauseDetailsFor(PauseRequest? request, HttpContext context)
    {
        if (request is null)
        {
            return null;
        }

        PauseDetails details = request.AsPauseDetails(AuthenticatedUser(context));
        AuditNote.Add(context, AuditNote.PauseReason(details));
        return details;
    }

    /// <summary>
    /// What a key-set pause body says, with the authenticated user as the requester when the body names
    /// none — or <see langword="null" /> when it names neither a reason nor a requester.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="PauseDetailsFor" />, a body with neither text is not put in the user's name: it is
    /// the body every client before 4.4 sends, so it stays the reasonless pause.
    /// </remarks>
    public static PauseDetails? KeySetPauseDetailsFor(TriggerKeySetPauseRequest request, HttpContext context)
    {
        PauseDetails? details = request.AsPauseDetails(AuthenticatedUser(context));
        AuditNote.Add(context, AuditNote.PauseReason(details));
        return details;
    }

    /// <inheritdoc cref="KeySetPauseDetailsFor(TriggerKeySetPauseRequest, HttpContext)" />
    public static PauseDetails? KeySetPauseDetailsFor(JobKeySetPauseRequest request, HttpContext context)
    {
        PauseDetails? details = request.AsPauseDetails(AuthenticatedUser(context));
        AuditNote.Add(context, AuditNote.PauseReason(details));
        return details;
    }

    /// <summary>
    /// The authenticated caller's name, or <see langword="null" /> for an anonymous or nameless one: the
    /// requester of a pause whose body names none.
    /// </summary>
    public static string? AuthenticatedUser(HttpContext context)
    {
        string? user = context.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(user) ? null : user;
    }

    public static void AssertIsValid(IValidatable toValidate)
    {
        var errors = toValidate.Validate().Distinct().ToArray();
        if (errors.Length == 0)
        {
            return;
        }

        var message = $"Request validation failed: {string.Join(", ", errors)}";
        throw new BadHttpRequestException(message);
    }

    /// <summary>
    /// Runs <paramref name="action" /> against the scheduler the route and the query name, or answers <c>404</c>.
    /// </summary>
    /// <inheritdoc cref="ExecuteWithResolution" path="/remarks" />
    public static Task<IResult> ExecuteWithScheduler(
        string schedulerName,
        string? target,
        HttpContext context,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask<IResult>> action)
    {
        return ExecuteWithResolution(schedulerName, target, context, schedulerRepository, resolution => action(resolution.Scheduler));
    }

    /// <summary>
    /// Runs <paramref name="action" /> against what the route and the query name resolve to, or answers
    /// <c>404</c>: for a route that has to know whether the scheduler is a window, or what its key is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is the route's name and, from 4.9, the query's <c>target</c>, and it resolves by the rule the
    /// dashboard's client resolves one by: <see cref="SchedulerLookup.Resolve" />, with this container's
    /// windows. A bare key resolves to the scheduler reached through no target — one of this process, a
    /// window, or an <c>AddQuartzHttpClient</c> registration without a target — as it always did, and a name
    /// held only by targets is refused naming them. A targeted key resolves to the proxy behind that target
    /// or the window onto the store attached under it, and the handler runs against that, so a request
    /// through a target is served by the same code as one to this process, the dashboard's own path: no
    /// bytes are relayed.
    /// </para>
    /// <para>
    /// A targeted proxy's failures are its target's: <see cref="TargetFaults" /> answers an unreachable one
    /// <c>503</c>, one that timed out <c>504</c>, its refusals <c>403</c> and its faults <c>502</c>, and never
    /// with the target's address. Authorization has already run by the time this does, so a caller who may
    /// not see a key learns nothing about whether it exists.
    /// </para>
    /// </remarks>
    public static async Task<IResult> ExecuteWithResolution(
        string schedulerName,
        string? target,
        HttpContext context,
        ISchedulerRepository schedulerRepository,
        Func<SchedulerResolution, ValueTask<IResult>> action)
    {
        SchedulerRef key = new(schedulerName, target);
        SchedulerResolution? resolved = SchedulerLookup.Resolve(schedulerRepository, context.RequestServices.GetService<SchedulerWindowRegistry>(), key);
        if (resolved is null)
        {
            // Still "Unknown scheduler", which HttpScheduler reads as a scheduler no request can reach.
            throw target is null
                ? NotFoundException.ForScheduler(schedulerName, SchedulerLookup.TargetsHolding(schedulerRepository, schedulerName))
                : NotFoundException.ForScheduler(key.Key);
        }

        if (target is null || resolved.Scheduler is not IProxyScheduler)
        {
            return await action(resolved).ConfigureAwait(false);
        }

        Exception fault;
        try
        {
            return await action(resolved).ConfigureAwait(false);
        }
        catch (Exception exception) when (TargetFaults.Translate(exception, resolved.Ref, IsMutation(context), context.RequestAborted) is { } translated)
        {
            // The answer the fault is, in place of what the transport raised: that names the target's
            // address, and the answer only its name.
            fault = translated;
        }

        throw fault;
    }

    public Task<IResult> ExecuteWithJsonResponse<T>(
        string schedulerName,
        string? target,
        HttpContext context,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask<T>> action) where T : notnull
    {
        return ExecuteWithScheduler(schedulerName, target, context, schedulerRepository, async scheduler =>
        {
            var response = await action(scheduler).ConfigureAwait(false);

            // An answer that says nothing was applied is entered as not done, as the dashboard enters a pause
            // or an interrupt that did not land.
            if (response is OperationAppliedResponse { Applied: false })
            {
                AuditNote.NotApplied(context);
            }

            return JsonResponse(response);
        });
    }

    public static Task<IResult> ExecuteWithOkResponse(
        string schedulerName,
        string? target,
        HttpContext context,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask> action)
    {
        return ExecuteWithScheduler(schedulerName, target, context, schedulerRepository, async scheduler =>
        {
            await action(scheduler).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    /// <summary>
    /// Whether the endpoint changes something, as <see cref="QuartzMutationMetadata" /> says — which a
    /// timed-out answer from a target then may have done.
    /// </summary>
    private static bool IsMutation(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<QuartzMutationMetadata>() is not null;
    }
}