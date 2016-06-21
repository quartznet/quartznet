using System;

namespace Quartz.Tests.Integration
{
    public class Program
    {
        public static int Main(string[] args)
        {
#if NETCORE
            return new NUnitLite.AutoRun(typeof(Program).GetTypeInfo().Assembly)
                .Execute(args, new NUnit.Common.ExtendedTextWrapper(Console.Out), Console.In);
#else
            Console.WriteLine("Please run with nunit runner");
            return 0;
#endif 
        }
    }
}