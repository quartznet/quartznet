using System.Net;
using System.Text.Json.Serialization.Metadata;

using FakeItEasy;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// An error is problem details in a host with no reflection-based serialization, too.
/// </summary>
/// <remarks>
/// A trimmed or native AOT publish switches reflection-based System.Text.Json off, and reflection was the
/// only thing that could write a <c>ProblemDetails</c> through the application's JSON options. So every
/// error the API answered went out as an empty <c>500</c>, and no trim warning said so (#3965). This test
/// process has reflection, so the host below takes the reflection resolver back out of the options once
/// Quartz has configured them, which leaves them as a trimmed host has them.
/// </remarks>
public sealed class ProblemDetailsWithoutReflectionTest
{
    private const string MissingJobRoute = $"schedulers/{TestData.SchedulerName}/jobs/canary/absent";

    private readonly List<WebApplicationFactory<Program>> factories = [];

    [TearDown]
    public async Task TearDown()
    {
        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public async Task AMissingJobIsANotFoundWithTheBodyReflectionWrites()
    {
        using HttpClient withoutReflection = Api(reflection: false);
        using HttpResponseMessage response = await withoutReflection.GetAsync(MissingJobRoute);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"a missing job is a 404 whether or not the host can reflect, and the client reads it as null; body was '{body}'");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using HttpClient withReflection = Api(reflection: true);
        using HttpResponseMessage reflected = await withReflection.GetAsync(MissingJobRoute);

        body.Should().Be(await reflected.Content.ReadAsStringAsync(),
            "the generated metadata has to write the same problem details reflection does, or a client would read a trimmed host differently");
    }

    [Test]
    public async Task AFaultIsAServerErrorThatSaysWhereItWent()
    {
        using HttpClient client = Api(reflection: false);
        using HttpResponseMessage response = await client.PostAsync($"schedulers/{TestData.SchedulerName}/pause-all", content: null);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.Should().Contain(ExceptionHandler.ServerFaultDetail,
            "a fault is written as problem details like every other error, rather than as a 500 that failed to write its own body");
    }

    private HttpClient Api(bool reflection)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (!reflection)
            {
                // After every IConfigureOptions, Quartz's among them.
                services.PostConfigure<JsonOptions>(options =>
                {
                    IList<IJsonTypeInfoResolver> chain = options.SerializerOptions.TypeInfoResolverChain;
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        if (chain[i] is DefaultJsonTypeInfoResolver)
                        {
                            chain.RemoveAt(i);
                        }
                    }
                });
            }
        }));
        factories.Add(configured);

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns(null);
        A.CallTo(() => fake.PauseAll(A<CancellationToken>._)).Throws(_ => new InvalidOperationException("the store is down"));

        HttpClient client = configured.CreateClient();

        ISchedulerRepository repository = configured.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
        return client;
    }
}
