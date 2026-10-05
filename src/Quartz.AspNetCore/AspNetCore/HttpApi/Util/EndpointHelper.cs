using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;
using Quartz.Extensibility;

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

        return request.AsPauseDetails(AuthenticatedUser(context));
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
        return request.AsPauseDetails(AuthenticatedUser(context));
    }

    /// <inheritdoc cref="KeySetPauseDetailsFor(TriggerKeySetPauseRequest, HttpContext)" />
    public static PauseDetails? KeySetPauseDetailsFor(JobKeySetPauseRequest request, HttpContext context)
    {
        return request.AsPauseDetails(AuthenticatedUser(context));
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

    public static async Task<IResult> ExecuteWithScheduler(
        string schedulerName,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask<IResult>> action)
    {
        var scheduler = schedulerRepository.Lookup(schedulerName);
        if (scheduler is null)
        {
            throw NotFoundException.ForScheduler(schedulerName);
        }

        return await action(scheduler).ConfigureAwait(false);
    }

    public Task<IResult> ExecuteWithJsonResponse<T>(
        string schedulerName,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask<T>> action) where T : notnull
    {
        return ExecuteWithScheduler(schedulerName, schedulerRepository, async scheduler =>
        {
            var response = await action(scheduler).ConfigureAwait(false);
            return JsonResponse(response);
        });
    }

    public static Task<IResult> ExecuteWithOkResponse(
        string schedulerName,
        ISchedulerRepository schedulerRepository,
        Func<IScheduler, ValueTask> action)
    {
        return ExecuteWithScheduler(schedulerName, schedulerRepository, async scheduler =>
        {
            await action(scheduler).ConfigureAwait(false);
            return Results.Ok();
        });
    }
}