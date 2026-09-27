using System.ComponentModel;

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
        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryCalendarNames), QueryCalendarNames)
            .WithQuartzDefaults(SchedulerRoutes.QueryCalendarNames, "Query calendar names");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetCalendar), GetCalendar)
            .WithQuartzDefaults(SchedulerRoutes.GetCalendar, "Get calendar details");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.CheckCalendarExists), CheckCalendarExists)
            .WithQuartzDefaults(SchedulerRoutes.CheckCalendarExists, "Check calendar exists");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.AddCalendar), AddCalendar)
            .WithQuartzDefaults(SchedulerRoutes.AddCalendar, "Add new calendar")
            .WithQuartzMutation(options);

        yield return builder.MapDelete(options.PatternFor(SchedulerRoutes.DeleteCalendar), DeleteCalendar)
            .WithQuartzDefaults(SchedulerRoutes.DeleteCalendar, "Delete calendar")
            .WithQuartzMutation(options);
    }

    [ProducesResponseType(typeof(PagedResultDto<string>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryCalendarNames(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        string? nameContains = null,
        string? nameEndsWith = null,
        string? nameStartsWith = null,
        string? nameEquals = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount) with
        {
            NameContains = nameContains,
            NameEndsWith = nameEndsWith,
            NameStartsWith = nameStartsWith,
            NameEquals = nameEquals
        };

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.QueryCalendarNames(scheduler, listing, cancellationToken));
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
            await SchedulerOperations.GetCalendar(scheduler, calendarName, cancellationToken).ConfigureAwait(false)
            ?? throw NotFoundException.ForCalendar(calendarName));
    }

    [ProducesResponseType(typeof(ExistsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> CheckCalendarExists(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.CheckCalendarExists(scheduler, calendarName, cancellationToken));
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.AddCalendar(scheduler, request, cancellationToken));
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteCalendar(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.DeleteCalendar(scheduler, calendarName, cancellationToken));
    }
}
