using System.Reflection;

using PublicApiGenerator;

namespace Quartz.Tests.AspNetCore;

/// <summary>
/// Snapshots the public API surface of the shipped assemblies that need ASP.NET Core to reference.
/// </summary>
/// <remarks>
/// The companion test in <c>Quartz.Tests.Unit</c> covers the rest. A failure here is not
/// automatically a bug: read the diff, and when the change is deliberate, accept the new baseline
/// and carry the same diff into <c>changelog.md</c> and the migration guide.
/// </remarks>
public class PublicApiTest
{
    private static readonly Assembly[] shippedAssemblies =
    [
        typeof(global::Quartz.AspNetCore.QuartzServiceCollectionExtensions).Assembly,
        typeof(global::Quartz.QuartzDashboardOptions).Assembly,
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
