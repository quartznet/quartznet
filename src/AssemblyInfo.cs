using System;
using System.Reflection;
using System.Runtime.InteropServices;

#if NET_40
[assembly: AssemblyConfiguration("net-4.0.win32; Release")]
#elif NET_35
[assembly: AssemblyConfiguration("net-3.5.win32; Release")]
#else
[assembly: AssemblyConfiguration("net-2.0.win32; Release")]
#endif

[assembly: AssemblyProduct("Quarz.NET 1.2")]
[assembly: AssemblyDescription("Quartz Scheduling Framework for .NET")]
[assembly: AssemblyCompany("http://quartznet.sourceforge.net/")]
[assembly: AssemblyCopyright("Copyright 2001-2009 Terracotta Inc. and partially Marko Lahma")]
[assembly: AssemblyTrademark("Apache License, Version 2.0")]
[assembly: AssemblyCulture("")]
[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

#if NET_40
[assembly: AssemblyVersion("1.2.0.4")]
#elif NET_35
[assembly: AssemblyVersion("1.2.0.3")]
#else
[assembly: AssemblyVersion("1.2.0.2")]
#endif

#if STRONG
[assembly: AssemblyDelaySign(false)]
[assembly: AssemblyKeyFile("Quartz.Net.snk")]
[assembly: System.Security.AllowPartiallyTrustedCallers]
#endif