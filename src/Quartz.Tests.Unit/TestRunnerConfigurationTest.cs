using System.Text.Json;

namespace Quartz.Tests.Unit;

/// <summary>
/// The <c>global.json</c> section that puts <c>dotnet test</c> in its Microsoft.Testing.Platform mode.
/// </summary>
/// <remarks>
/// The build's test targets pass MTP-mode arguments, and <c>dotnet test</c> in VSTest mode refuses a test
/// project on MTP 2, so without the section no test run works (#3970). JetBrains Rider 2026.1 and 2026.2
/// delete it whenever their "Manage .NET SDK" dialog rewrites the file (RIDER-140778, fixed in 2026.3).
/// </remarks>
public class TestRunnerConfigurationTest
{
    [Test]
    public void GlobalJsonSelectsTheMicrosoftTestingPlatformRunner()
    {
        string path = Path.Combine(RepositoryRoot.Find().FullName, "global.json");
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(path));

        string runner = globalJson.RootElement.TryGetProperty("test", out JsonElement test)
            && test.TryGetProperty("runner", out JsonElement value)
                ? value.GetString()
                : null;

        runner.Should().Be("Microsoft.Testing.Platform",
            "global.json needs \"test\": { \"runner\": \"Microsoft.Testing.Platform\" } for dotnet test to run these "
            + "projects at all. Rider 2026.1 and 2026.2 delete that section when their Manage .NET SDK dialog saves "
            + "the file (RIDER-140778): restore it rather than changing the build");
    }
}
