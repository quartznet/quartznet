namespace Quartz.Diagnostics;

public static class OperationName
{
    public static class Job
    {
        public const string Execute = "Quartz.Job.Execute";
        public const string Veto = "Quartz.Job.Vetoed";
    }
}