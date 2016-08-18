// Provide TypeFilterLevel enum values for use on .NET Core (they aren't used by the .NET Framework on Core, but
// will keep public surface area the same between desktop and Core builds of Quartz.Net).
// 
// When running on a platform that already exposes the enum (desktop .NET, for example), redirect references
// to the actual implementation.

#if REMOTING
[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(System.Runtime.Serialization.Formatters.TypeFilterLevel))]
#else // REMOTING
namespace System.Runtime.Serialization.Formatters
{
    //
    // Summary:
    //     Specifies the level of automatic deserialization for .NET Framework remoting.
    public enum TypeFilterLevel
    {
        //
        // Summary:
        //     The low deserialization level for .NET Framework remoting. It supports types
        //     associated with basic remoting functionality.
        Low = 2,
        //
        // Summary:
        //     The full deserialization level for .NET Framework remoting. It supports all types
        //     that remoting supports in all situations.
        Full = 3
    }
}
#endif // REMOTING