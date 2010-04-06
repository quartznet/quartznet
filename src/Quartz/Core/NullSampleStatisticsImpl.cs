using System;

namespace Quartz.Core
{
    public class NullSampledStatisticsImpl : ISampledStatistics
    {
        public long JobsScheduledMostRecentSample
        {
            get { return 0; }
        }

        public long JobsExecutingMostRecentSample
        {
            get { return 0; }
        }

        public long JobsCompletedMostRecentSample
        {
            get { return 0; }
        }
    }
}