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
        public override Task SchedulerErrorAsync(string msg, SchedulerException cause)
        {
            Log.ErrorException(msg, cause);
            return TaskUtil.CompletedTask;
        }
    }
}