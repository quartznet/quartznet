namespace Quartz.Logging;

public static class DiagnosticHeaders
{
    public const string DefaultListenerName = "Quartz";

    public const string SchedulerName = "scheduler.name";
    public const string SchedulerId = "scheduler.id";
    public const string FireInstanceId = "fire.instance.id";
    public const string TriggerGroup = "trigger.group";
    public const string TriggerName = "trigger.name";
    public const string JobType = "job.type";
    public const string JobGroup = "job.group";
    public const string JobName = "job.name";
}