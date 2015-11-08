using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    [RoutePrefix("api/schedulers/{schedulerName}/calendars")]
    public class CalendarsController : ApiController
    {
        [HttpGet]
        [Route("")]
        public async Task<IReadOnlyList<string>> Calendars(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var calendarNames = await scheduler.GetCalendarNamesAsync().ConfigureAwait(false);

            return calendarNames;
        }

        [HttpGet]
        [Route("{calendarName}")]
        public async Task<CalendarDetailDto> CalendarDetails(string schedulerName, string calendarName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var calendar = await scheduler.GetCalendarAsync(calendarName).ConfigureAwait(false);
            return CalendarDetailDto.Create(calendar);
        }

        [HttpPut]
        [Route("{calendarName}")]
        public async Task AddCalendar(string schedulerName, string calendarName, bool replace, bool updateTriggers)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            ICalendar calendar = null;
            await scheduler.AddCalendarAsync(calendarName, calendar, replace, updateTriggers).ConfigureAwait(false);
        }

        [HttpDelete]
        [Route("{calendarName}")]
        public async Task DeleteCalendar(string schedulerName, string calendarName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.DeleteCalendarAsync(calendarName).ConfigureAwait(false);
        }

        private static async Task<IScheduler> GetScheduler(string schedulerName)
        {
            var scheduler = await SchedulerRepository.Instance.Lookup(schedulerName).ConfigureAwait(false);
            if (scheduler == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
            return scheduler;
        }
    }
}