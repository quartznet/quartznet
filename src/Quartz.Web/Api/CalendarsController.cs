using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    [Route("api/schedulers/{schedulerName}/calendars")]
    public class CalendarsController : Controller
    {
        [HttpGet]
        [Route("")]
        public async Task<IReadOnlyList<string>> Calendars(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var calendarNames = await scheduler.GetCalendarNames().ConfigureAwait(false);

            return calendarNames;
        }

        [HttpGet]
        [Route("{calendarName}")]
        public async Task<CalendarDetailDto> CalendarDetails(string schedulerName, string calendarName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var calendar = await scheduler.GetCalendar(calendarName).ConfigureAwait(false);
            return CalendarDetailDto.Create(calendar);
        }

        [HttpPut]
        [Route("{calendarName}")]
        public async Task AddCalendar(string schedulerName, string calendarName, bool replace, bool updateTriggers)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            ICalendar calendar = null;
            await scheduler.AddCalendar(calendarName, calendar, replace, updateTriggers).ConfigureAwait(false);
        }

        [HttpDelete]
        [Route("{calendarName}")]
        public async Task DeleteCalendar(string schedulerName, string calendarName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.DeleteCalendar(calendarName).ConfigureAwait(false);
        }

        private static async Task<IScheduler> GetScheduler(string schedulerName)
        {
            var scheduler = await SchedulerRepository.Instance.Lookup(schedulerName).ConfigureAwait(false);
            if (scheduler == null)
            {
                //throw new HttpResponseException(HttpStatusCode.NotFound);
                throw new KeyNotFoundException($"Scheduler {schedulerName} not found!");
            }
            return scheduler;
        }
    }
}