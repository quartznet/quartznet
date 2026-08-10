using Microsoft.AspNetCore.Http;

using Quartz.HttpApiContract;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class EndpointHelper
{
    public static IResult JsonResponse(object data) => Results.Json(data);

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
    /// The most keys one bulk fetch request may carry.
    /// </summary>
    public const int MaxKeysToFetch = 1000;

    public static void AssertPaging(int skip, int take)
    {
        if (skip < 0)
        {
            throw new BadHttpRequestException("skip must not be negative");
        }

        if (take < 0)
        {
            throw new BadHttpRequestException("take must not be negative");
        }
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

    public static Task<IResult> ExecuteWithJsonResponse<T>(
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