using Microsoft.AspNetCore.Http;

using Quartz.HttpApiContract;
using Quartz.Impl.Matchers;
using Quartz.Spi;
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