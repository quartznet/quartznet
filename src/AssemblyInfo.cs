using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.

#if NET_35
[assembly: AssemblyConfiguration("net-3.5.win32; Release")]
#elif NET_20
[assembly: AssemblyConfiguration("net-2.0.win32; Release")]
#else
[assembly: AssemblyConfiguration("net-1.1.win32; Release")]
#endif

[assembly: AssemblyProduct("Quarz.NET 1.0")]
[assembly: AssemblyDescription("Quartz Scheduling Framework for .NET")]
[assembly : AssemblyCompany("http://quartznet.sourceforge.net/")]
[assembly : AssemblyCopyright("Copyright 2001-2009 James House and partially Marko Lahma")]
[assembly:  AssemblyTrademark("Apache License, Version 2.0")]
[assembly : AssemblyCulture("")]
//[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

#if NET_35
[assembly: AssemblyVersion("1.0.2.3")]
#elif NET_20
[assembly: AssemblyVersion("1.0.2.2")]
#else
[assembly: AssemblyVersion("1.0.2.1")]
#endif

#if STRONG
[assembly: AssemblyDelaySign(false)]
[assembly: AssemblyKeyFile("Quartz.Net.snk")]
[assembly: AllowPartiallyTrustedCallers]
#endif