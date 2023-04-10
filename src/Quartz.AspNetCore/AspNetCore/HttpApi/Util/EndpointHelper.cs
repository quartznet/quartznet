using Microsoft.AspNetCore.Http;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Util;

namespace Quartz.AspNetCore.HttpApi.Util;

internal class EndpointHelper
{
    public IResult JsonResponse(object data) => Results.Json(data);

    public GroupMatcher<T> GetGroupMatcher<T>(string? groupContains, string? groupEndsWith, string? groupStartsWith, string? groupEquals) where T : Key<T>
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

    public void AssertIsValid(IValidatable toValidate)
    {
        var errors = toValidate.Validate().Distinct().ToArray();
        if (errors.Length == 0)
        {
            return;
        }

        var message = $"Request validation failed: {string.Join(", ", errors)}";
        throw new BadHttpRequestException(message);
    }

    public async Task<IResult> ExecuteWithScheduler(string schedulerName, Func<IScheduler, Task<IResult>> action)
    {
        var scheduler = await SchedulerRepository.Instance.Lookup(schedulerName).ConfigureAwait(false);
        if (scheduler == null)
        {
            throw NotFoundException.ForScheduler(schedulerName);
        }

        return await action(scheduler).ConfigureAwait(false);
    }

    public Task<IResult> ExecuteWithJsonResponse<T>(string schedulerName, Func<IScheduler, Task<T>> action) where T : notnull
    {
        return ExecuteWithScheduler(schedulerName, async scheduler =>
        {
            var response = await action(scheduler).ConfigureAwait(false);
            return JsonResponse(response);
        });
    }

    public Task<IResult> ExecuteWithOkResponse(string schedulerName, Func<IScheduler, Task> action)
    {
        return ExecuteWithScheduler(schedulerName, async scheduler =>
        {
            await action(scheduler).ConfigureAwait(false);
            return Results.Ok();
        });
    }
}