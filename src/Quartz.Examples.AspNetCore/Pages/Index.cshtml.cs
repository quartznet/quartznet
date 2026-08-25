using Microsoft.AspNetCore.Mvc.RazorPages;


namespace Quartz.Examples.AspNetCore.Pages;

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

    public IReadOnlyCollection<TriggerKey> Triggers { get; set; } = [];
    public IReadOnlyCollection<FireInstance> CurrentlyExecutingJobs { get; set; } = [];

    public async Task OnGet()
    {
        var scheduler = await schedulerFactory.GetScheduler();
        Triggers = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
        CurrentlyExecutingJobs = (await scheduler.QueryFireInstances(new FireInstanceQuery())).Items;
    }
}