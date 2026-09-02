using System.Reflection;

using PublicApiGenerator;

namespace Quartz.Tests.Unit;

/// <summary>
/// Snapshots the public API surface of the shipped assemblies this project can reference.
/// </summary>
/// <remarks>
/// <c>Quartz.AspNetCore</c> and <c>Quartz.Dashboard</c> are covered by the test of the same name in
/// <c>Quartz.Tests.AspNetCore</c>, which is where their dependencies already live. Between the two,
/// every packable project has a baseline — add one here or there when a new package is added, which
/// <see cref="EveryShippedPackageHasAPublicApiBaseline" /> holds you to.
/// </remarks>
/// <remarks>
/// This is the only guard the repository has against unintended public API changes — there is no
/// ApiCompat run and no shipped/unshipped API files. A failure here is not automatically a bug:
/// read the diff, and when the change is deliberate, accept the new baseline and carry the same
/// diff into the migration guide. When it is not deliberate, the diff is the bug report.
/// </remarks>
/// <remarks>
/// The rendering says more than <c>PublicApiGenerator</c> alone would: records carry the
/// <c>record</c> keyword, an interface member with a default implementation carries a marker, and
/// the explicit interface implementations of a type are listed. <see cref="PublicApiRendering" />
/// explains why. The one shape it still cannot show is a positional record's <c>Deconstruct</c>,
/// which the compiler marks as generated and the generator therefore drops — the primary
/// constructor is in the baseline, so the change is visible, but not by that name.
/// </remarks>
public class PublicApiTest
{
    private static readonly Assembly[] shippedAssemblies =
    [
        typeof(global::Quartz.IScheduler).Assembly,
        typeof(global::Quartz.QuartzAspireSettings).Assembly,
        typeof(global::Quartz.Jobs.DirectoryScanJob).Assembly,
        typeof(global::Quartz.Plugins.History.LoggingJobHistoryPlugin).Assembly,
        typeof(global::Quartz.TimeZonePluginConfigurationExtensions).Assembly,
        typeof(global::Quartz.Serialization.Newtonsoft.Calendars.ICalendarSerializer).Assembly,
        typeof(global::Quartz.HttpScheduler).Assembly,
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

            // A record is not a class: it brings value equality, a copy constructor and `with`, and
            // turning one into the other breaks callers without changing a single signature.
            TreatRecordsAsClasses = false,
        };

        var publicApi = PublicApiRendering.Annotate(assembly.GeneratePublicApi(options), assembly);

        await Verify(publicApi, extension: "txt")
            .UseDirectory("Verify")
            .UseFileName($"PublicApiTest_{name}")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// Every project that produces a package has a baseline, in one of the two <c>Verify</c>
    /// directories that hold them.
    /// </summary>
    /// <remarks>
    /// The list above is hand-maintained and split across two test projects, so until this test the
    /// coverage was a convention: an eleventh package would have shipped with no baseline and a green
    /// suite, and the only guard the repository has against an unintended public API change would have
    /// been silently absent from it. This asks the tree rather than a list —
    /// <see cref="ShippedProjects" /> computes the packable set from the csproj files — so a new
    /// package fails here on the day it is added, with the file it is missing named.
    /// </remarks>
    [TestCaseSource(nameof(PackableProjects))]
    public void EveryShippedPackageHasAPublicApiBaseline(string packageName)
    {
        DirectoryInfo root = RepositoryRoot.Find();

        string[] candidates =
        [
            Path.Combine(root.FullName, "src", "Quartz.Tests.Unit", "Verify", $"PublicApiTest_{packageName}.verified.txt"),
            Path.Combine(root.FullName, "src", "Quartz.Tests.AspNetCore", "Verify", $"PublicApiTest_{packageName}.verified.txt"),
        ];

        candidates.Where(File.Exists).Should().ContainSingle(
            $"{packageName} ships to nuget.org, so its public surface is snapshotted — add the assembly to "
            + "the list in PublicApiTest (or in the test of the same name in Quartz.Tests.AspNetCore, where "
            + "the ASP.NET Core dependencies live) and accept the baseline it writes");
    }

    private static IEnumerable<TestCaseData> PackableProjects() =>
        ShippedProjects.Find()
            .Select(x => Path.GetFileNameWithoutExtension(x.Name))
            .Select(x => new TestCaseData(x).SetArgDisplayNames(x));
}
