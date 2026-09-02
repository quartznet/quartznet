using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;
using Quartz.Extensibility;
using Quartz.Util;

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
    }

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

    public static GroupMatcher<T> GetGroupMatcher<T>(string? groupContains, string? groupEndsWith, string? groupStartsWith, string? groupEquals) where T : Key<T>
    {
        // Allow only single value to be given
        var givenValueCount = new[] { groupContains, groupEndsWith, groupStartsWith, groupEquals }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (givenValueCount > 1)
        {
            throw new BadHttpRequestException("Only single match rule can be given");
        }

        if (!string.IsNullOrWhiteSpace(groupContains))
        {
            return GroupMatcher<T>.GroupContains(groupContains);
        }

        if (!string.IsNullOrWhiteSpace(groupEndsWith))
        {
            return GroupMatcher<T>.GroupEndsWith(groupEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(groupStartsWith))
        {
            return GroupMatcher<T>.GroupStartsWith(groupStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(groupEquals))
        {
            return GroupMatcher<T>.GroupEquals(groupEquals);
        }

        return GroupMatcher<T>.AnyGroup();
    }

    /// <summary>
    /// Builds the name filter a listing request asked for, or null when it asked for none — a name
    /// filter is optional, where the group filter always ends up as "any group".
    /// </summary>
    public static NameMatcher<T>? GetNameMatcher<T>(string? nameContains, string? nameEndsWith, string? nameStartsWith, string? nameEquals) where T : Key<T>
    {
        // Allow only single value to be given
        var givenValueCount = new[] { nameContains, nameEndsWith, nameStartsWith, nameEquals }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (givenValueCount > 1)
        {
            throw new BadHttpRequestException("Only single match rule can be given");
        }

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            return NameMatcher<T>.NameContains(nameContains);
        }

        if (!string.IsNullOrWhiteSpace(nameEndsWith))
        {
            return NameMatcher<T>.NameEndsWith(nameEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(nameStartsWith))
        {
            return NameMatcher<T>.NameStartsWith(nameStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(nameEquals))
        {
            return NameMatcher<T>.NameEquals(nameEquals);
        }

        return null;
    }

    /// <summary>
    /// The counterpart of <see cref="GetNameMatcher{T}" /> for the listings whose subject is named
    /// rather than keyed — calendars and groups — so their filter is a <see cref="NameMatcher" />,
    /// spelled the same way on the wire.
    /// </summary>
    public static NameMatcher? GetNameMatcher(string? nameContains, string? nameEndsWith, string? nameStartsWith, string? nameEquals)
    {
        // Allow only single value to be given
        var givenValueCount = new[] { nameContains, nameEndsWith, nameStartsWith, nameEquals }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (givenValueCount > 1)
        {
            throw new BadHttpRequestException("Only single match rule can be given");
        }

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            return NameMatcher.NameContains(nameContains);
        }

        if (!string.IsNullOrWhiteSpace(nameEndsWith))
        {
            return NameMatcher.NameEndsWith(nameEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(nameStartsWith))
        {
            return NameMatcher.NameStartsWith(nameStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(nameEquals))
        {
            return NameMatcher.NameEquals(nameEquals);
        }

        return null;
    }

    /// <summary>
    /// The most keys one bulk fetch request may carry.
    /// </summary>
    public const int MaxKeysToFetch = 1000;

    /// <summary>
    /// Reads the paging a listing request carried, answering the <c>take</c> to apply or
    /// <see langword="null" /> when the request named none and the query record's own default should
    /// stand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>take</c> is bound as a string rather than an <see cref="int" /> so that
    /// <c>?take=all</c> can mean <see cref="PagedQuery.All" />. Asking for everything is a real thing
    /// to want — an export, a group-name list, a migration — and the number behind it is
    /// <c>2147483647</c>, which reads in a URL as a mistake rather than as an intention. The number
    /// is still accepted, so a client that sends one keeps working; only the spelling is new.
    /// </para>
    /// <para>
    /// <see cref="QuartzHttpApiOptions.MaxPageSize" /> bounds both spellings, and answers them
    /// differently on purpose. A number is a request the server can meet or cannot, so one above the cap
    /// is a <c>400</c> naming it. <c>all</c> is not a number — it says "as many as you will give me" —
    /// so it is answered with the cap, and <c>hasMore</c> says whether that was all of them. Which means
    /// a listing whose matches fit under the cap answers exactly as it would with no cap at all: this is
    /// what every <c>SchedulerQueryExtensions</c> listing asks for through <c>HttpScheduler</c>,
    /// three matches or three million, and a cap that refused it would refuse a three-job listing.
    /// </para>
    /// </remarks>
    /// <exception cref="BadHttpRequestException">
    /// <paramref name="skip" /> is negative, or <paramref name="take" /> is negative, is neither a
    /// number nor the "everything" sentinel, or is a number above
    /// <see cref="QuartzHttpApiOptions.MaxPageSize" />.
    /// </exception>
    public int? ParsePaging(int skip, string? take)
    {
        if (skip < 0)
        {
            throw new BadHttpRequestException("skip must not be negative");
        }

        if (string.IsNullOrWhiteSpace(take))
        {
            return null;
        }

        if (string.Equals(take, HttpApiConstants.AllItems, StringComparison.OrdinalIgnoreCase))
        {
            return maxPageSize > 0 ? maxPageSize : PagedQuery.All;
        }

        if (!int.TryParse(take, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new BadHttpRequestException(
                $"take must be a number or '{HttpApiConstants.AllItems}', which asks for every match");
        }

        if (parsed < 0)
        {
            throw new BadHttpRequestException("take must not be negative");
        }

        if (maxPageSize > 0 && parsed > maxPageSize)
        {
            throw new BadHttpRequestException(
                $"take must be at most {maxPageSize}, was {parsed.ToString(CultureInfo.InvariantCulture)}. "
                + $"Ask for '{HttpApiConstants.AllItems}' to take as many as the server allows, page the request, "
                + $"or raise {nameof(QuartzHttpApiOptions)}.{nameof(QuartzHttpApiOptions.MaxPageSize)}.");
        }

        return parsed;
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
        Func<IScheduler, Task<IResult>> action)
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
        Func<IScheduler, Task<T>> action) where T : notnull
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
        Func<IScheduler, Task> action)
    {
        return ExecuteWithScheduler(schedulerName, schedulerRepository, async scheduler =>
        {
            await action(scheduler).ConfigureAwait(false);
            return Results.Ok();
        });
    }
}