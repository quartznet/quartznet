using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using Quartz.Logging;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.History
{
    /// <summary>
    /// Web API endpoint for job history. Requires persistent storage to work with.
    /// </summary>
    public class JobExecutionHistoryController : Controller
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof (JobExecutionHistoryController));

        [HttpGet]
        [Route("api/schedulers/{schedulerName}/jobs/history")]
        public async Task<JobHistoryViewModel> SchedulerHistory(string schedulerName)
        {
            var jobHistoryDelegate = DatabaseExecutionHistoryPlugin.Delegate;
            IReadOnlyList<JobHistoryEntryDto> entries = new List<JobHistoryEntryDto>();
            string errorMessage = null;

            try
            {
                entries = await jobHistoryDelegate.SelectJobHistoryEntries(schedulerName).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log.ErrorException("Error while retrieving history entries",  e);
                errorMessage = e.Message;
            }
            var model = new JobHistoryViewModel(entries, errorMessage);
            return model;
        }
    }
}