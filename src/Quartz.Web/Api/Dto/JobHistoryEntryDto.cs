namespace Quartz.Web.Api.Dto;

public class JobHistoryEntryDto
{
    public required string JobName { get; set; }
    public required string JobGroup { get; set; }
    public required string TriggerName { get; set; }
    public required string TriggerGroup { get; set; }
    public required DateTimeOffset FiredTime { get; set; }
    public required DateTimeOffset ScheduledTime { get; set; }
    public required TimeSpan RunTime { get; set; }
    public required bool Error { get; set; }
    public required string ErrorMessage { get; set; }
}