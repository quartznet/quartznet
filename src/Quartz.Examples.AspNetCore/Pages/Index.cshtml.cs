using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;

namespace Quartz.Examples.AspNetCore.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ISchedulerFactory schedulerFactory;

        public IndexModel(
            ILogger<IndexModel> logger,
            ISchedulerFactory schedulerFactory)
        {
            _logger = logger;
            this.schedulerFactory = schedulerFactory;
        }

        public IReadOnlyCollection<TriggerKey> Triggers { get; set; } = Array.Empty<TriggerKey>();
        public IReadOnlyCollection<IJobExecutionContext> CurrentlyExecutingJobs { get; set; } = Array.Empty<IJobExecutionContext>();

        public async Task OnGet()
        {
            var scheduler = await schedulerFactory.GetScheduler();
            Triggers = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
            CurrentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs();
        }
    }
}