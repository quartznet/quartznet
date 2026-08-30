#nullable enable

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// What ships in <c>Quartz.Aspire</c>, and what an application referencing it drags in.
/// </summary>
/// <remarks>
/// The whole argument for this package taking no <c>Aspire.*</c> dependency is that a client integration
/// needs none: <see cref="Microsoft.Extensions.Hosting.IHostApplicationBuilder"/> is the contract, and
/// <c>Aspire.Hosting</c> alone brings roughly thirty packages — <c>Grpc.AspNetCore</c>,
/// <c>KubernetesClient</c>, <c>YamlDotNet</c>, <c>Newtonsoft.Json</c> — under this repository's transitive
/// pinning. It is an argument that has to be enforced, because a single <c>PackageReference</c> undoes it.
/// </remarks>
public class AspirePackagingTest
{
    private static FileInfo Project => new(Path.Combine(
        RepositoryRoot.Find().FullName, "src", "Quartz.Aspire", "Quartz.Aspire.csproj"));

    private static FileInfo Schema => new(Path.Combine(
        RepositoryRoot.Find().FullName, "src", "Quartz.Aspire", "ConfigurationSchema.json"));

    [Test]
    public void ThePackageTakesNoAspireDependency()
    {
        IEnumerable<string> aspire = XDocument.Load(Project.FullName)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? "")
            .Where(name => name.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, "Aspire", StringComparison.OrdinalIgnoreCase));

        aspire.Should().BeEmpty(
            "a client integration needs no Aspire package, and taking one would tie this package's "
            + "supported versions to Aspire's release cadence for no type it uses");
    }

    [Test]
    public void ThePackageTakesNoFrameworkReference()
    {
        XDocument.Load(Project.FullName).Descendants("FrameworkReference").Should().BeEmpty(
            "a framework reference reaches the nuspec, and a worker on a dotnet/runtime image is exactly "
            + "who this package is for - which is what #3532 was about");
    }

    [Test]
    public void TheConfigurationSchemaAndItsTargetsArePacked()
    {
        List<XElement> packed = XDocument.Load(Project.FullName)
            .Descendants("None")
            .Where(element => string.Equals((string?) element.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<string?> included = packed.Select(element => (string?) element.Attribute("Include")).ToList();

        included.Should().Contain("ConfigurationSchema.json",
            "an IDE completes an Aspire section in appsettings.json from the schema in the package root");
        included.Should().Contain(@"buildTransitive\net10.0\Quartz.Aspire.targets",
            "the schema is inert without the JsonSchemaSegment item that points the SDK at it");
    }

    [Test]
    public void TheTargetsFilePointsAtTheSchemaInThePackageRoot()
    {
        FileInfo targets = new(Path.Combine(
            RepositoryRoot.Find().FullName, "src", "Quartz.Aspire", "buildTransitive", "net10.0", "Quartz.Aspire.targets"));

        targets.Exists.Should().BeTrue();

        XElement segment = XDocument.Load(targets.FullName).Descendants("JsonSchemaSegment").Single();

        ((string?) segment.Attribute("Include")).Should().Be(
            @"$(MSBuildThisFileDirectory)..\..\ConfigurationSchema.json",
            "the targets land in buildTransitive/net10.0, so two directories up is the package root");
        ((string?) segment.Attribute("FilePathPattern")).Should().Be(@"appsettings\..*json");
    }

    /// <summary>
    /// The schema is hand-written, because Aspire's generator for it has never shipped
    /// (microsoft/aspire#3309). So nothing but this holds it to the type it describes.
    /// </summary>
    [Test]
    public void EverySettingAppearsInTheConfigurationSchema()
    {
        Schema.Exists.Should().BeTrue();

        JsonNode? described = JsonNode.Parse(File.ReadAllText(Schema.FullName))
            ?["properties"]?["Aspire"]?["properties"]?["Quartz"]?["properties"];

        described.Should().NotBeNull(
            "the schema describes Aspire:Quartz, which is the section the settings are bound from");

        List<string> documented = described!.AsObject().Select(property => property.Key).Order(StringComparer.Ordinal).ToList();

        List<string> settings = typeof(QuartzAspireSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        documented.Should().BeEquivalentTo(settings,
            "the schema is what an IDE completes and validates an appsettings.json against, so a setting "
            + "missing from it is a setting nobody discovers, and one left in it after the property is "
            + "gone is a suggestion that does nothing");
    }

    [Test]
    public void TheConfigurationSchemaIsValidJsonWithADescriptionOnEverySetting()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Schema.FullName));

        JsonElement described = document.RootElement
            .GetProperty("properties").GetProperty("Aspire")
            .GetProperty("properties").GetProperty("Quartz")
            .GetProperty("properties");

        foreach (JsonProperty setting in described.EnumerateObject())
        {
            setting.Value.TryGetProperty("description", out JsonElement description).Should().BeTrue(
                $"{setting.Name} shows its description in the IDE's completion list, and one without is a "
                + "name the reader has to go and look up");
            description.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
