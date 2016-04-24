using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;

using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    /// <summary>
    /// Web API endpoint for scheduler information.
    /// </summary>
    [RoutePrefix("api/schedulers")]
    public class SchedulerController : ApiController
    {
        [HttpGet]
        [Route("")]
        public async Task<IReadOnlyList<SchedulerHeaderDto>> AllSchedulers()
        {
            var schedulers = await SchedulerRepository.Instance.LookupAll().ConfigureAwait(false);
            return schedulers.Select(x => new SchedulerHeaderDto(x)).ToList();
        }

        [HttpGet]
        [Route("{schedulerName}")]
        public async Task<SchedulerDto> SchedulerDetails(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var metaData = await scheduler.GetMetaData().ConfigureAwait(false);
            return new SchedulerDto(scheduler, metaData);
        }

        [HttpPost]
        [Route("{schedulerName}/start")]
        public async Task Start(string schedulerName, int? delayMilliseconds = null)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            if (delayMilliseconds == null)
            {
                await scheduler.Start().ConfigureAwait(false);
            }
            else
            {
                await scheduler.StartDelayed(TimeSpan.FromMilliseconds(delayMilliseconds.Value)).ConfigureAwait(false);
            }
        }

        [HttpPost]
        [Route("{schedulerName}/standby")]
        public async Task Standby(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.Standby().ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{schedulerName}/shutdown")]
        public async Task Shutdown(string schedulerName, bool waitForJobsToComplete = false)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.Shutdown(waitForJobsToComplete).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{schedulerName}/clear")]
        public async Task Clear(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.Clear().ConfigureAwait(false);
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