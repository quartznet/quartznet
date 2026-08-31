using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Extensibility;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class CalendarEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/calendars";

        yield return builder.MapGet(patternPrefix, QueryCalendarNames)
            .WithQuartzDefaults(nameof(QueryCalendarNames), "Query calendar names");

        yield return builder.MapGet(patternPrefix + "/{calendarName}", GetCalendar)
            .WithQuartzDefaults(nameof(GetCalendar), "Get calendar details");

        yield return builder.MapGet(patternPrefix + "/{calendarName}/exists", CheckCalendarExists)
            .WithQuartzDefaults(nameof(CheckCalendarExists), "Check calendar exists");

        yield return builder.MapPost(patternPrefix, AddCalendar)
            .WithQuartzDefaults(nameof(AddCalendar), "Add new calendar");

        yield return builder.MapDelete(patternPrefix + "/{calendarName}", DeleteCalendar)
            .WithQuartzDefaults(nameof(DeleteCalendar), "Delete calendar");
    }

    [ProducesResponseType(typeof(PagedResultDto<string>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryCalendarNames(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        int? take = null,
        bool includeTotalCount = false,
        string? nameContains = null,
        string? nameEndsWith = null,
        string? nameStartsWith = null,
        string? nameEquals = null,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertPaging(skip, take);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            CalendarQuery query = new()
            {
                Skip = skip,
                IncludeTotalCount = includeTotalCount,
                Name = EndpointHelper.GetNameMatcher(nameContains, nameEndsWith, nameStartsWith, nameEquals)
            };

            // a request that names no take gets the query record's own default page size
            if (take.HasValue)
            {
                query = query with { Take = take.Value };
            }

            PagedResult<string> page = await scheduler.QueryCalendarNames(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<string>(page.Items.ToArray(), page.HasMore, page.TotalCount);
        });
    }

    [ProducesResponseType(typeof(OpenApi.Calendar), StatusCodes.Status200OK)]
    private static Task<IResult> GetCalendar(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var calendar = await scheduler.GetCalendarOrThrow(calendarName, cancellationToken).ConfigureAwait(false);
            return calendar;
        });
    }

    [ProducesResponseType(typeof(ExistsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> CheckCalendarExists(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            bool exists = await scheduler.Exists(calendarName, cancellationToken).ConfigureAwait(false);
            return new ExistsResponse(exists);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.AddCalendarRequest), "application/json")]
    private static Task<IResult> AddCalendar(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        AddCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return EndpointHelper.ExecuteWithOkResponse(
            schedulerName,
            schedulerRepository,
            scheduler => scheduler.AddCalendar(
                request.CalendarName,
                request.Calendar,
                new AddCalendarOptions { Replace = request.Replace, UpdateTriggers = request.UpdateTriggers },
                cancellationToken).AsTask()
        );
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteCalendar(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var calendarFound = await scheduler.DeleteCalendar(calendarName, cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(calendarFound);
        });
    }
}