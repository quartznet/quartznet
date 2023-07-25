using Microsoft.AspNetCore.Mvc;

using Quartz.Logging;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.History;

/// <summary>
/// Web API endpoint for job history. Requires persistent storage to work with.
/// </summary>
public class JobExecutionHistoryController : Controller
{
    private static readonly ILogger<JobExecutionHistoryController> log = LogProvider.CreateLogger<JobExecutionHistoryController>();

    [HttpGet]
    [Route("api/schedulers/{schedulerName}/jobs/history")]
    public async Task<JobHistoryViewModel> SchedulerHistory(string schedulerName)
    {
        var jobHistoryDelegate = DatabaseExecutionHistoryPlugin.Delegate;
        IReadOnlyList<JobHistoryEntryDto> entries = new List<JobHistoryEntryDto>();
        string errorMessage = string.Empty;

        try
        {
            entries = await jobHistoryDelegate.SelectJobHistoryEntries(schedulerName).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log.LogError(e,"Error while retrieving history entries");
            errorMessage = e.Message;
        }
        var model = new JobHistoryViewModel(entries, errorMessage);
        return model;
    }
}