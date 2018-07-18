namespace Quartz.Impl.RavenDB
{
    internal enum SchedulerState
    {
        Initialized,
        Started,
        Paused,
        Resumed,
        Shutdown,
    }
}