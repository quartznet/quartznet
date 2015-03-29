using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        public IList<SchedulerHeaderDto> AllSchedulers()
        {
            var schedulers = SchedulerRepository.Instance.LookupAll();
            return schedulers.Select(x => new SchedulerHeaderDto(x)).ToList();
        }

        [HttpGet]
        [Route("{schedulerName}")]
        public SchedulerDto SchedulerDetails(string schedulerName)
        {
            var scheduler = GetScheduler(schedulerName);
            return new SchedulerDto(scheduler);
        }

        [HttpPost]
        [Route("{schedulerName}/start")]
        public void Start(string schedulerName, int? delayMilliseconds = null)
        {
            var scheduler = GetScheduler(schedulerName);
            if (delayMilliseconds == null)
            {
                scheduler.Start();
            }
            else
            {
                scheduler.StartDelayed(TimeSpan.FromMilliseconds(delayMilliseconds.Value));
            }
        }

        [HttpPost]
        [Route("{schedulerName}/standby")]
        public void Standby(string schedulerName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.Standby();
        }

        [HttpPost]
        [Route("{schedulerName}/shutdown")]
        public void Shutdown(string schedulerName, bool waitForJobsToComplete = false)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.Shutdown(waitForJobsToComplete);
        }

        [HttpPost]
        [Route("{schedulerName}/clear")]
        public void Clear(string schedulerName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.Clear();
        }

        private static IScheduler GetScheduler(string schedulerName)
        {
            var scheduler = SchedulerRepository.Instance.Lookup(schedulerName);
            if (scheduler == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
            return scheduler;
        }
    }
}