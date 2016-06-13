using System;
using System.Reflection;

using NUnit.Common;

namespace Quartz.Tests.Integration
{
    public class Program
    {
        public static int Main(string[] args)
        {
#if NETCORE
            return new NUnitLite.AutoRun(typeof(Program).GetTypeInfo().Assembly)
                .Execute(args, new ExtendedTextWrapper(Console.Out), Console.In);
#else
            Console.WriteLine("Please run with nunit runner");
            return 0;
#endif 
        }
    }
}