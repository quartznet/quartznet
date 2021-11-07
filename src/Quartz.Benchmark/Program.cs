using System.Runtime.CompilerServices;

using BenchmarkDotNet.Running;

namespace Quartz.Benchmark
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

            //DispatchBenchmark();
        }

        private static void DispatchBenchmark()
        {
            var benchmark = new JobDispatchBenchmark();
            benchmark.Run().GetAwaiter().GetResult();

            RunDispatch(benchmark);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunDispatch(JobDispatchBenchmark benchmark)
        {
            for (int i = 0; i < 100; ++i)
            {
                benchmark.Run().GetAwaiter().GetResult();
            }
        }
    }
}