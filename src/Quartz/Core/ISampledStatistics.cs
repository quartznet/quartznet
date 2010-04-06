namespace Quartz.Core
{
    public interface ISampledStatistics
    {
        long JobsScheduledMostRecentSample { get; }
        long JobsExecutingMostRecentSample { get; }
        long JobsCompletedMostRecentSample { get; }
    }
}