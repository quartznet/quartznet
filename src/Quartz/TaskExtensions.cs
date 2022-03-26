using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Quartz
{
    internal static class TaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompletedSuccessfully(this Task t)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
            return t.IsCompletedSuccessfully;
#else
            return t.Status == TaskStatus.RanToCompletion;
#endif
        }
    }
}