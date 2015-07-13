using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
        public async Task<IReadOnlyList<KeyDto>> Jobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            var jobKeys = await scheduler.GetJobKeys(matcher).ConfigureAwait(false);
            return jobKeys.Select(x => new KeyDto(x)).ToList();
        }

        [HttpGet]
        [Route("{jobGroup}/{jobName}/details")]
        public async Task<JobDetailDto> JobDetails(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var jobDetail = await scheduler.GetJobDetail(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
            return new JobDetailDto(jobDetail);
        }

        [HttpGet]
        [Route("currently-executing/{bar}")]
        public async Task<List<CurrentlyExecutingJobDto>> CurrentlyExecutingJobs(string schedulerName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var currentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs().ConfigureAwait(false);
            return currentlyExecutingJobs.Select(x => new CurrentlyExecutingJobDto(x)).ToList();
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/pause")]
        public async Task PauseJobs(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.PauseJob(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("pause")]
        public async Task PauseJobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            await scheduler.PauseJobs(matcher).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/resume")]
        public async Task ResumeJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.ResumeJob(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("resume")]
        public async Task ResumeJobs(string schedulerName, GroupMatcherDto groupMatcher)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            var matcher = (groupMatcher ?? new GroupMatcherDto()).GetJobGroupMatcher();
            await scheduler.ResumeJobs(matcher).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/trigger")]
        public async Task TriggerJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.TriggerJob(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/delete")]
        public async Task DeleteJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.DeleteJob(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
        }

        [HttpPost]
        [Route("{jobGroup}/{jobName}/interrupt")]
        public async Task InterruptJob(string schedulerName, string jobGroup, string jobName)
        {
            var scheduler = await GetScheduler(schedulerName).ConfigureAwait(false);
            await scheduler.Interrupt(new JobKey(jobName, jobGroup)).ConfigureAwait(false);
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