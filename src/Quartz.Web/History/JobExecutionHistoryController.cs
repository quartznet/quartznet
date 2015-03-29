using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

using Quartz.Web.Api.Dto;

namespace Quartz.Web.History
{
    /// <summary>
    /// Web API endpoint for job history. Requires persistent storage to work with.
    /// </summary>
    public class JobExecutionHistoryController : ApiController
    {
        [HttpGet]
        [Route("api/schedulers/{schedulerName}/jobs/history")]
        public async Task<IList<JobHistoryEntryDto>> SchedulerHistory(string schedulerName)
        {
            var jobHistoryDelegate = DatabaseExecutionHistoryPlugin.Delegate;
            return await jobHistoryDelegate.SelectJobHistoryEntries(schedulerName);
        }
    }
}