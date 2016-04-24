using System.Threading.Tasks;

using Quartz.Listener;
using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Core
{
    /// <summary>
    /// ErrorLogger - Scheduler Listener Class
    /// </summary>
    internal class ErrorLogger : SchedulerListenerSupport
    {
        public override Task SchedulerError(string msg, SchedulerException cause)
        {
            Log.ErrorException(msg, cause);
            return TaskUtil.CompletedTask;
        }
    }
}