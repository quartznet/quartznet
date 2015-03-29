using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;

using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    [RoutePrefix("api/schedulers/{schedulerName}/jobs")]
    public class JobsController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IList<KeyDto> Jobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            var jobKeys = scheduler.GetJobKeys(matcher);
            return jobKeys.Select(x => new KeyDto(x)).ToList();
        }

        [HttpGet]
        [Route("{jobGroup}/{jobName}/details")]
        public JobDetailDto JobDetails(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            var jobDetail = scheduler.GetJobDetail(new JobKey(jobName, jobGroup));
            return new JobDetailDto(jobDetail);
        }

        [HttpGet]
        [Route("currently-executing/{bar}")]
        public List<CurrentlyExecutingJobDto> CurrentlyExecutingJobs(string schedulerName)
        {
            var scheduler = GetScheduler(schedulerName);
            var currentlyExecutingJobs = scheduler.GetCurrentlyExecutingJobs();
            return currentlyExecutingJobs.Select(x => new CurrentlyExecutingJobDto(x)).ToList();
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/pause")]
        public void PauseJobs(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.PauseJob(new JobKey(jobName, jobGroup));
        }

        [HttpPost]
        [Route("pause")]
        public void PauseJobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            scheduler.PauseJobs(matcher);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/resume")]
        public void ResumeJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.ResumeJob(new JobKey(jobName, jobGroup));
        }

        [HttpPost]
        [Route("resume")]
        public void ResumeJobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = GetScheduler(schedulerName);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            scheduler.ResumeJobs(matcher);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/trigger")]
        public void TriggerJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.TriggerJob(new JobKey(jobName, jobGroup));
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/delete")]
        public void DeleteJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.DeleteJob(new JobKey(jobName, jobGroup));
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/interrupt")]
        public void InterruptJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = GetScheduler(schedulerName);
            scheduler.Interrupt(new JobKey(jobName, jobGroup));
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