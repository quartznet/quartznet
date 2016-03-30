// Provide ThreadInterruptedException type for use on .NET Core (the exception type will never be thrown, but
// allows for moving old code that caught the exception more easily - and less messily - than #if'ing out all
// occurrences of the type).
// 
// When running on a platform that already exposes the type (desktop .NET, for example), redirect references
// to the actual implementation.

#if THREAD_INTERRUPTION
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Threading.ThreadInterruptedException))]
#else // THREAD_INTERRUPTION
namespace System.Threading
{
    public class ThreadInterruptedException : Exception
    {
        public ThreadInterruptedException() { }
        public ThreadInterruptedException(string message) { }
        public ThreadInterruptedException(string message, Exception innerException) { }
    }
}
#endif // THREAD_INTERRUPTION