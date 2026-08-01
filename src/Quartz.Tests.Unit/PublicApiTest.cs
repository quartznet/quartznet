#if NETCORE
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using PublicApiGenerator;

using VerifyNUnit;

namespace Quartz.Tests.Unit;

/// <summary>
/// Snapshots the public API surface of the shipped assemblies this project can reference.
/// </summary>
/// <remarks>
/// <c>Quartz.AspNetCore</c> and <c>Quartz.Dashboard</c> are covered by the test of the same name in
/// <c>Quartz.Tests.AspNetCore</c>, which is where their dependencies already live. Between the two,
/// every packable project has a baseline — add one here or there when a new package is added.
/// </remarks>
/// <remarks>
/// <c>Quartz.OpenTelemetry.Instrumentation</c> is the one packable project without a baseline.
/// Referencing it drags in <c>OpenTelemetry 0.6.0-beta.1</c>, which fails restore under this
/// repository's warnings-as-errors with two NU1608 constraint violations and the NU1902 advisory
/// GHSA-g94r-2vxg-569j. Buying a snapshot of two frozen types is not worth suppressing a
/// vulnerability warning for; the package is gone in 4.x in favour of
/// <c>OpenTelemetry.Instrumentation.Quartz</c>. If it is ever brought up to a current
/// OpenTelemetry, add it back here.
/// </remarks>
/// <remarks>
/// This is the only guard the repository has against unintended public API changes — there is no
/// ApiCompat run and no shipped/unshipped API files. A failure here is not automatically a bug:
/// read the diff, and when the change is deliberate, accept the new baseline. When it is not
/// deliberate, the diff is the bug report. 3.x is the maintenance branch, so a removal or a changed
/// signature in one of these baselines is a breaking change and needs saying out loud in the PR.
/// </remarks>
/// <remarks>
/// The baselines are taken on <c>net10.0</c> only, which is why the whole file sits behind
/// <c>NETCORE</c>. The <c>net472</c> build of <c>Quartz</c> has a different surface — <c>REMOTING</c>
/// adds <c>RemoteScheduler</c> and friends — and snapshotting both would double every file here for
/// a surface that is frozen anyway.
/// </remarks>
public class PublicApiTest
{
    private static readonly Assembly[] shippedAssemblies =
    [
        typeof(global::Quartz.IScheduler).Assembly,
        typeof(global::Quartz.Job.DirectoryScanJob).Assembly,
        typeof(global::Quartz.Plugin.History.LoggingJobHistoryPlugin).Assembly,
        typeof(global::Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin).Assembly,
        typeof(global::Quartz.Simpl.JsonObjectSerializer).Assembly,
        typeof(global::Quartz.Simpl.SystemTextJsonObjectSerializer).Assembly,
        typeof(global::Quartz.IServiceCollectionQuartzConfigurator).Assembly,
        typeof(global::Quartz.QuartzHostedService).Assembly,
        typeof(global::Quartz.RedisLockHandlerConfigurationExtensions).Assembly,
        typeof(global::Quartz.OpenTracing.QuartzDiagnosticOptions).Assembly,
    ];

    private static IEnumerable<TestCaseData> Assemblies()
    {
        foreach (Assembly assembly in shippedAssemblies)
        {
            yield return new TestCaseData(assembly).SetName(assembly.GetName().Name);
        }
    }

    [TestCaseSource(nameof(Assemblies))]
    public async Task PublicApiHasNotChangedUnintentionally(Assembly assembly)
    {
        string name = assembly.GetName().Name!;

        ApiGeneratorOptions options = new ApiGeneratorOptions
        {
            // These say how the compiler encoded something, not what the contract is, and they
            // churn the baseline whenever an unrelated file is touched.
            ExcludeAttributes =
            [
                "System.Diagnostics.DebuggerDisplayAttribute",
                "System.Reflection.AssemblyMetadataAttribute",
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                "System.Runtime.CompilerServices.IsReadOnlyAttribute",
                "System.Runtime.CompilerServices.NullableAttribute",
                "System.Runtime.CompilerServices.NullableContextAttribute",
                "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
                "System.Runtime.Versioning.TargetFrameworkAttribute",
            ],
        };

        string publicApi = assembly.GeneratePublicApi(options);

        // Deliberately no AutoVerify under a debugger, unlike JsonObjectSerializerTest: accepting an
        // API baseline is a decision, and one that should never be made by simply attaching.
        await Verifier.Verify(publicApi, extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"PublicApiTest_{name}")
            .DisableRequireUniquePrefix();
    }
}
#endif
