using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Quartz.Core;

namespace Quartz.Benchmark;

/// <summary>
/// Which runtime this process runs on, which of Quartz's two assets it loaded, and whether that asset's
/// fire path is compiled with runtime async (#4214).
/// </summary>
/// <remarks>
/// <para>
/// Quartz ships <c>net10.0</c> and <c>net11.0</c>, and only <c>net11.0</c> is compiled with
/// <c>runtime-async=on</c>. This project's <c>net10.0</c> build loads the <c>net10.0</c> asset, on .NET 10 or
/// rolled forward to .NET 11, and its <c>net11.0</c> build loads the <c>net11.0</c> one.
/// Nothing in a benchmark's output says which, so each leg of a comparison between them is checked with
/// this run first, from the same build and under the same environment as its measured runs.
/// </para>
/// <para>
/// The check is <c>RuntimeAsyncTest</c>'s. <see cref="JobRunShell.Run" /> carries
/// <c>MethodImplAttributes.Async</c> (0x2000) and no <see cref="AsyncStateMachineAttribute" /> when the
/// runtime suspends and resumes it, and the reverse when the compiler wrote a state machine for it.
/// </para>
/// </remarks>
internal static class RuntimeInfo
{
    /// <summary><c>MethodImplAttributes.Async</c>, which .NET 11 declares and <c>net10.0</c> does not.</summary>
    private const MethodImplAttributes Async = (MethodImplAttributes) 0x2000;

    /// <summary>Prints the runtime, both assets and how the fire path's entry point was compiled.</summary>
    public static void Run()
    {
        Assembly benchmark = typeof(RuntimeInfo).Assembly;
        Assembly quartz = typeof(JobRunShell).Assembly;
        MethodInfo run = typeof(JobRunShell).GetMethod(nameof(JobRunShell.Run), [typeof(CancellationToken)])
            ?? throw new InvalidOperationException("JobRunShell.Run(CancellationToken) is gone, and it is the method this check reads.");

        bool runtimeAsync = (run.MethodImplementationFlags & Async) == Async;
        AsyncStateMachineAttribute? stateMachine = run.GetCustomAttribute<AsyncStateMachineAttribute>();

        string compiled = (runtimeAsync, stateMachine) switch
        {
            (true, null) => "runtime async: MethodImplAttributes.Async is set and there is no state machine",
            (false, not null) => $"compiler state machine {stateMachine.StateMachineType.Name}: MethodImplAttributes.Async is not set",
            _ => $"neither: MethodImplAttributes.Async is {(runtimeAsync ? "set" : "not set")} and the state machine is {stateMachine?.StateMachineType.Name ?? "absent"}",
        };

        Console.WriteLine($"Runtime          {RuntimeInformation.FrameworkDescription} ({Environment.Version}), {RuntimeInformation.ProcessArchitecture}, "
                          + $"{(GCSettings.IsServerGC ? "server" : "workstation")} GC, {GCSettings.LatencyMode}");
        Console.WriteLine($"Benchmark asset  {FrameworkOf(benchmark)}  {benchmark.Location}");
        Console.WriteLine($"Quartz asset     {FrameworkOf(quartz)}  {quartz.Location}  "
                          + $"{quartz.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Console.WriteLine($"JobRunShell.Run  {compiled}");

        (int asyncFlagged, int stateMachines) = CountAsyncMethods(quartz);
        Console.WriteLine($"Whole assembly   {asyncFlagged} methods with MethodImplAttributes.Async, {stateMachines} with AsyncStateMachineAttribute");
    }

    /// <summary>
    /// How many of an assembly's methods the runtime suspends and resumes itself, and how many the compiler
    /// wrote a state machine for. One asset is all of one and none of the other.
    /// </summary>
    private static (int AsyncFlagged, int StateMachines) CountAsyncMethods(Assembly assembly)
    {
        const BindingFlags everything = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        int asyncFlagged = 0;
        int stateMachines = 0;
        foreach (Type type in assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(everything))
            {
                if ((method.MethodImplementationFlags & Async) == Async)
                {
                    asyncFlagged++;
                }

                if (method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false))
                {
                    stateMachines++;
                }
            }
        }

        return (asyncFlagged, stateMachines);
    }

    /// <summary>
    /// One line saying what this process measures: the runtime, the Quartz asset it loaded and how that asset's
    /// fire path is compiled, and the environment the thread-pool and roll-forward settings come from.
    /// </summary>
    /// <remarks>
    /// The fire-throughput benchmarks print it from <c>[GlobalSetup]</c>, which runs in the measured process, so
    /// that a job of <see cref="RuntimeComparison" /> is proved from the inside rather than from its name.
    /// </remarks>
    public static string Describe()
    {
        Assembly quartz = typeof(JobRunShell).Assembly;
        MethodInfo? run = typeof(JobRunShell).GetMethod(nameof(JobRunShell.Run), [typeof(CancellationToken)]);
        bool runtimeAsync = run is not null && (run.MethodImplementationFlags & Async) == Async;
        bool stateMachine = run?.GetCustomAttribute<AsyncStateMachineAttribute>() is not null;
        string compiled = (runtimeAsync, stateMachine) switch
        {
            (true, false) => "runtime async",
            (false, true) => "state machines",
            _ => $"unclear (Async flag {runtimeAsync}, state machine {stateMachine})",
        };

        string? rollForward = Environment.GetEnvironmentVariable("DOTNET_ROLL_FORWARD");
        string? spinThreshold = Environment.GetEnvironmentVariable("DOTNET_ThreadPool_SpuriousDispatchNoSpinThreshold");
        string? osr = Environment.GetEnvironmentVariable("DOTNET_TC_OnStackReplacement");
        string? callCountingDelay = Environment.GetEnvironmentVariable("DOTNET_TC_CallCountingDelayMs");

        return $"Measured process: .NET {Environment.Version}, Quartz {FrameworkOf(quartz)} asset with {compiled}, "
               + $"DOTNET_ROLL_FORWARD={rollForward ?? "-"}, DOTNET_ThreadPool_SpuriousDispatchNoSpinThreshold={spinThreshold ?? "-"}, "
               + $"DOTNET_TC_OnStackReplacement={osr ?? "-"}, DOTNET_TC_CallCountingDelayMs={callCountingDelay ?? "-"}, "
               + $"{quartz.Location}";
    }

    /// <summary>The framework an assembly was built for, as <c>net11.0</c>.</summary>
    private static string FrameworkOf(Assembly assembly)
    {
        string? name = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        if (name is null)
        {
            return "unknown";
        }

        Version version = new FrameworkName(name).Version;
        return $"net{version.Major}.{version.Minor}";
    }
}
