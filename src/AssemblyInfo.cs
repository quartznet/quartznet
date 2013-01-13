using System;
using System.Reflection;
using System.Runtime.InteropServices;

#if NET_40
#if DEBUG
[assembly: AssemblyConfiguration("net-4.0.win32; Debug")]
#else
[assembly: AssemblyConfiguration("net-4.0.win32; Release")]
#endif
#else
#if DEBUG
[assembly: AssemblyConfiguration("net-3.5.win32; Debug")]
#else
[assembly: AssemblyConfiguration("net-3.5.win32; Release")]
#endif
#endif

#if NET_40
[assembly: AssemblyProduct("Quarz.NET 2.1.2 for .NET 4.0")]
#else
[assembly: AssemblyProduct("Quarz.NET 2.1.2 for .NET 3.5 SP1")]
#endif
[assembly: AssemblyDescription("Quartz Scheduling Framework for .NET")]
[assembly: AssemblyCompany("http://www.quartz-scheduler.net/")]
[assembly: AssemblyCopyright("Copyright 2001-2013 Terracotta Inc. and Marko Lahma")]
[assembly: AssemblyTrademark("Apache License, Version 2.0")]
[assembly: AssemblyCulture("")]
[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

[assembly: AssemblyVersion("2.1.2.400")]

[assembly: AssemblyDelaySign(false)]
#if !NET_40
[assembly: System.Security.AllowPartiallyTrustedCallers]
#endif