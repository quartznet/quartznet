using System.Reflection;

using Microsoft.AspNetCore.Components;

using PublicApiGenerator;

namespace Quartz.Tests.AspNetCore;

/// <summary>
/// Snapshots the public API surface of the shipped assemblies that need ASP.NET Core to reference.
/// </summary>
/// <remarks>
/// The companion test in <c>Quartz.Tests.Unit</c> covers the rest. A failure here is not
/// automatically a bug: read the diff, and when the change is deliberate, accept the new baseline
/// and carry the same diff into the migration guide.
/// </remarks>
public class PublicApiTest
{
    private static readonly Assembly[] shippedAssemblies =
    [
        typeof(global::Quartz.QuartzAspNetCoreConfigurationExtensions).Assembly,
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

            // Blazor components are the dashboard's UI, not API anyone calls. The .razor compiler
            // emits the class — so it cannot be made internal — along with a BuildRenderTree
            // override whose body is the markup, which means every markup edit would otherwise land
            // in this baseline as if it were a contract change. What the dashboard actually offers
            // its consumers (options, extension methods, the model types) is not a component and
            // stays snapshotted.
            ExcludeTypes = assembly.GetExportedTypes()
                .Where(static type => typeof(ComponentBase).IsAssignableFrom(type))
                .ToArray(),
        };

        var publicApi = assembly.GeneratePublicApi(options);

        await Verify(publicApi, extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"PublicApiTest_{name}")
            .DisableRequireUniquePrefix();
    }
}
