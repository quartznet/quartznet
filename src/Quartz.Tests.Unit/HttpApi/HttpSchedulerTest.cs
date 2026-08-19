using System.Text.Json;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The serializer options handed to the scheduler are borrowed, not owned.
/// </summary>
public class HttpSchedulerTest
{
    private HttpClient httpClient;

    [SetUp]
    public void SetUp()
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
    }

    [TearDown]
    public void TearDown()
    {
        httpClient?.Dispose();
        httpClient = null;
    }

    [Test]
    public void ShouldAcceptSerializerOptionsThatHaveAlreadyBeenUsed()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        // Serializing with the options freezes them: anything writing to them from here on throws.
        JsonSerializer.Serialize(42, options);
        options.IsReadOnly.Should().BeTrue("the rest of this test is vacuous unless the options are frozen");

        var act = () => new HttpScheduler("Scheduler", httpClient, options);

        act.Should().NotThrow("the caller's options are copied before Quartz's converters go on");
    }

    [Test]
    public void ShouldNotAddItsConvertersToTheSuppliedSerializerOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        _ = new HttpScheduler("Scheduler", httpClient, options);
        _ = new HttpScheduler("SecondScheduler", httpClient, options);

        options.Converters.Should().BeEmpty(
            "the caller's options instance comes back untouched, so two clients sharing one cannot stack converters onto it");
    }
}
