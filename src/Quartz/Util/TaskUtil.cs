using System.Threading.Tasks;

namespace Quartz.Util
{
    /// <summary>
    /// Internal helpers for working with tasks.
    /// </summary>
    public static class TaskUtil
    {
        public static readonly Task CompletedTask = Task.FromResult(true);
    }
}