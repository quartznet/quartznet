﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class CalendarEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/calendars";

        yield return builder.MapGet(patternPrefix, GetCalendarNames)
            .WithQuartzDefaults(nameof(GetCalendarNames), "Get calendar names");

        yield return builder.MapGet(patternPrefix + "/{calendarName}", GetCalendar)
            .WithQuartzDefaults(nameof(GetCalendar), "Get calendar details");

        yield return builder.MapPost(patternPrefix, AddCalendar)
            .WithQuartzDefaults(nameof(AddCalendar), "Add new calendar");

        yield return builder.MapDelete(patternPrefix + "/{calendarName}", DeleteCalendar)
            .WithQuartzDefaults(nameof(DeleteCalendar), "Delete calendar");
    }

    [ProducesResponseType(typeof(NamesDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetCalendarNames(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, async scheduler =>
        {
            var calendarNames = await scheduler.GetCalendarNames(cancellationToken).ConfigureAwait(false);
            return new NamesDto(calendarNames);
        });
    }

    [ProducesResponseType(typeof(OpenApi.Calendar), StatusCodes.Status200OK)]
    private static Task<IResult> GetCalendar(
        EndpointHelper endpointHelper,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, async scheduler =>
        {
            var calendar = await scheduler.GetCalendarOrThrow(calendarName, cancellationToken).ConfigureAwait(false);
            return calendar;
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.AddCalendarRequest), "application/json")]
    private static Task<IResult> AddCalendar(
        EndpointHelper endpointHelper,
        string schedulerName,
        AddCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithOkResponse(
            schedulerName,
            scheduler => scheduler.AddCalendar(request.CalendarName, request.Calendar, request.Replace, request.UpdateTriggers, cancellationToken)
        );
    }

    [ProducesResponseType(typeof(DeleteCalendarResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteCalendar(
        EndpointHelper endpointHelper,
        string schedulerName,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, async scheduler =>
        {
            var calendarFound = await scheduler.DeleteCalendar(calendarName, cancellationToken).ConfigureAwait(false);
            return new DeleteCalendarResponse(calendarFound);
        });
    }
}