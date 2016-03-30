// Provide ThreadPriority enum values for use on .NET Core (they aren't used by the .NET Framework on Core, but
// can be used by Quartz.Net internally).
// 
// When running on a platform that already exposes the enum (desktop .NET, for example), redirect references
// to the actual implementation.

#if THREAD_PRIORITY
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Threading.ThreadPriority))]
#else // THREAD_PRIORITY
namespace System.Threading
{
    //
    // Summary:
    //     Specifies the scheduling priority of a System.Threading.Thread.
    public enum ThreadPriority
    {
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with any other priority.
        Lowest = 0,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Normal priority
        //     and before those with Lowest priority.
        BelowNormal = 1,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with AboveNormal priority
        //     and before those with BelowNormal priority. Threads have Normal priority by default.
        Normal = 2,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Highest priority
        //     and before those with Normal priority.
        AboveNormal = 3,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled before threads with any other priority.
        Highest = 4
    }
}
#endif // THREAD_PRIORITY