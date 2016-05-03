// Provide ApartmentState enum values for use on .NET Core (they aren't used by the .NET Framework on Core, but
// will keep public surface area the same between desktop and Core builds of Quartz.Net).
// 
// When running on a platform that already exposes the enum (desktop .NET, for example), redirect references
// to the actual implementation.

#if THREAD_APARTMENTSTATE
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Threading.ApartmentState))]
#else // THREAD_APARTMENTSTATE
namespace System.Threading
{
    //
    // Summary:
    //     Specifies the apartment state of a System.Threading.Thread.
    public enum ApartmentState
    {
        //
        // Summary:
        //     The System.Threading.Thread will create and enter a single-threaded apartment.
        STA = 0,
        //
        // Summary:
        //     The System.Threading.Thread will create and enter a multithreaded apartment.
        MTA = 1,
        //
        // Summary:
        //     The System.Threading.Thread.ApartmentState property has not been set.
        Unknown = 2
    }
}
#endif // THREAD_APARTMENTSTATE