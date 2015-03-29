using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;

using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    [RoutePrefix("api/schedulers/{schedulerName}/triggers")]
    public class TriggersController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IList<KeyDto> Triggers(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetTriggerGroupMatcher();
            var jobKeys = scheduler.GetTriggerKeys(matcher);

            return jobKeys.Select(x => new KeyDto(x)).ToList();
        }

        [HttpGet]
        [Route("{triggerGroup}/{triggerName}/details")]
        public TriggerDetailDto TriggerDetails(string schedulerName, string triggerGroup, string triggerName)
        {
            var scheduler = GetScheduler(schedulerName);
            var trigger = scheduler.GetTrigger(new TriggerKey(triggerName, triggerGroup));
            return new TriggerDetailDto(scheduler, trigger);
        }

        [HttpPost]
        [Route("{triggerGroup}/{triggerName}/pause")]
        public void PauseTrigger(string schedulerName, string triggerGroup, string triggerName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.PauseTrigger(new TriggerKey(triggerName, triggerGroup));
        }

        [HttpPost]
        [Route("pause")]
        public void PauseTriggers(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetTriggerGroupMatcher();
            scheduler.PauseTriggers(matcher);
        }

        [HttpPost]
        [Route("{triggerGroup}/{triggerName}/resume")]
        public void ResumeTrigger(string schedulerName, string triggerGroup, string triggerName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.ResumeTrigger(new TriggerKey(triggerName, triggerGroup));
        }

        [HttpPost]
        [Route("resume")]
        public void ResumeTriggers(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetTriggerGroupMatcher();
            scheduler.ResumeTriggers(matcher);
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