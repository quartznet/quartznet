using PublicApiGenerator;

namespace Quartz.Tests.Aspire;

/// <summary>
/// Snapshots the public API of <c>Quartz.Aspire.Hosting</c>, the one shipped assembly that needs
/// <c>Aspire.Hosting</c> to reference.
/// </summary>
/// <remarks>
/// The companion tests in <c>Quartz.Tests.Unit</c> and <c>Quartz.Tests.AspNetCore</c> cover the rest and
/// say what a failure means: read the diff, and when the change is deliberate, accept the new baseline and
/// carry it into the migration guide.
/// </remarks>
public sealed class PublicApiTest
{
    [Test]
    public async Task PublicApiHasNotChangedUnintentionally()
    {
        System.Reflection.Assembly assembly = typeof(global::Aspire.Hosting.QuartzDashboardResourceBuilderExtensions).Assembly;

        ApiGeneratorOptions options = new()
        {
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
            TreatRecordsAsClasses = false,
        };

        string publicApi = PublicApiRendering.Annotate(assembly.GeneratePublicApi(options), assembly);

        await Verify(publicApi, extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"PublicApiTest_{assembly.GetName().Name}")
            .DisableRequireUniquePrefix();
    }
}
