using System.Reflection;

using PublicApiGenerator;

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
/// This is the only guard the repository has against unintended public API changes — there is no
/// ApiCompat run and no shipped/unshipped API files. A failure here is not automatically a bug:
/// read the diff, and when the change is deliberate, accept the new baseline and carry the same
/// diff into the migration guide. When it is not deliberate, the diff is the bug report.
/// </remarks>
public class PublicApiTest
{
    private static readonly Assembly[] shippedAssemblies =
    [
        typeof(global::Quartz.IScheduler).Assembly,
        typeof(global::Quartz.Jobs.DirectoryScanJob).Assembly,
        typeof(global::Quartz.Plugin.History.LoggingJobHistoryPlugin).Assembly,
        typeof(global::Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin).Assembly,
        typeof(global::Quartz.Serialization.Newtonsoft.ICalendarSerializer).Assembly,
        typeof(global::Quartz.HttpClient.HttpScheduler).Assembly,
        typeof(global::Quartz.RedisLockHandlerConfigurationExtensions).Assembly,
    ];

    private static IEnumerable<TestCaseData> Assemblies()
    {
        foreach (var assembly in shippedAssemblies)
        {
            yield return new TestCaseData(assembly).SetName(assembly.GetName().Name);
        }
    }

    [TestCaseSource(nameof(Assemblies))]
    public async Task PublicApiHasNotChangedUnintentionally(Assembly assembly)
    {
        var name = assembly.GetName().Name;

        var options = new ApiGeneratorOptions
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

        var publicApi = assembly.GeneratePublicApi(options);

        await Verify(publicApi, extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"PublicApiTest_{name}")
            .DisableRequireUniquePrefix();
    }
}
