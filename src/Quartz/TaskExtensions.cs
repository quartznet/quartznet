using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Quartz
{
    internal static class TaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompletedSuccessfully(this Task t)
        {
            return t.Status == TaskStatus.RanToCompletion && !t.IsFaulted && !t.IsCanceled;
        }   
    }
}