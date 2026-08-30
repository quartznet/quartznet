#nullable enable

using System.Reflection;
using System.Xml.Linq;

namespace Quartz.Tests.Unit.HealthChecks;

/// <summary>
/// Where the health check ships, and what a package taking it drags in.
/// </summary>
/// <remarks>
/// <c>Quartz.AspNetCore</c> carries <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c>
/// for the HTTP API, and a framework reference reaches the nuspec — so a worker on a
/// <c>dotnet/runtime</c> image that referenced that package for the health check alone failed to start
/// (#3532). The check itself reads <c>IScheduler.Status</c> and probes the store, and wants nothing from
/// ASP.NET Core, so it lives in the core package now.
/// </remarks>
public class HealthCheckPackagingTest
{
    [Test]
    public void TheHealthCheckShipsInTheCorePackage()
    {
        Assembly core = typeof(IScheduler).Assembly;

        typeof(QuartzHealthCheckOptions).Assembly.Should().BeSameAs(core);
        core.GetType("Quartz.QuartzHealthCheck", throwOnError: false).Should().NotBeNull(
            "the check itself is internal, so only its assembly-qualified name says where it lives");

        typeof(QuartzHealthCheckExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .Should().Contain(["AddQuartz", "AddQuartzHealthChecks"],
                "registering the check must not need a reference beyond the core package");
    }

    [Test]
    public void TheCorePackageTakesNoFrameworkReference()
    {
        FileInfo project = new(Path.Combine(
            RepositoryRoot.Find().FullName, "src", "Quartz", "Quartz.csproj"));

        List<string> references = XDocument.Load(project.FullName)
            .Descendants("FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value ?? "")
            .ToList();

        references.Should().BeEmpty(
            "a framework reference reaches the nuspec, so one here would stop every consumer of Quartz "
            + "from running on a dotnet/runtime image — which is the whole of #3532");
    }
}
