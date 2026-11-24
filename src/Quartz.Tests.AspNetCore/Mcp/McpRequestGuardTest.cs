using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.AspNetCore.TestHost;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M9: the endpoint refuses a browser's request from an origin it does not list, and a body that is not declared
/// JSON, before the SDK reads either; an agent's request passes, and no session is minted.
/// </summary>
public sealed class McpRequestGuardTest
{
    private const string ToolsList = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

    private McpTestHost? host;

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }
    }

    [Test]
    public async Task AnOriginNotListedIsRefusedBeforeTheSdk()
    {
        host = await McpTestHost.Start(writable: false, mcp: options => options.AllowedOrigins.Add("https://ops.example.com"));

        using HttpResponseMessage response = await Post(ToolsList, "application/json", origin: "https://evil.example");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        host.Logs.WithEventId(9504).Should().ContainSingle();
    }

    [Test]
    public async Task AListedOriginAndNoOriginPass()
    {
        host = await McpTestHost.Start(writable: false, mcp: options => options.AllowedOrigins.Add("https://ops.example.com"));

        using HttpResponseMessage listed = await Post(ToolsList, "application/json", origin: "https://ops.example.com");
        using HttpResponseMessage agent = await Post(ToolsList, "application/json", origin: null);

        listed.StatusCode.Should().Be(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());
        agent.StatusCode.Should().Be(HttpStatusCode.OK);
        agent.Headers.Contains("Mcp-Session-Id").Should().BeFalse("the endpoint is stateless, and binds no session to anyone");
    }

    [Test]
    public async Task EveryOriginIsRefusedWhenNoneIsListed()
    {
        host = await McpTestHost.Start(writable: false);

        using HttpResponseMessage response = await Post(ToolsList, "application/json", origin: "http://localhost:3000");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the MCP specification's DNS rebinding check is on by default");
    }

    [Test]
    public async Task ABodyNotDeclaredJsonIsRefusedWithUnsupportedMediaType()
    {
        host = await McpTestHost.Start(writable: false);

        using HttpResponseMessage response = await Post(ToolsList, "text/plain", origin: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "text/plain is CORS-safelisted, so refusing it is what makes every request that gets through one a browser had to preflight");
    }

    /// <summary>
    /// The SDK's endpoint says it accepts <c>application/json</c>, which routing holds a declared type to; a body that
    /// declares none passes routing, and the guard is what refuses it.
    /// </summary>
    [Test]
    public async Task ABodyThatDeclaresNoTypeIsRefusedByTheGuard()
    {
        host = await McpTestHost.Start(writable: false);

        using HttpResponseMessage response = await Post(ToolsList, contentType: null, origin: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        host.Logs.WithEventId(9504).Should().ContainSingle().Which.Properties["Reason"].Should().Contain("not application/json");
    }

    private async Task<HttpResponseMessage> Post(string body, string? contentType, string? origin)
    {
        HttpClient client = host!.App.GetTestClient();
        ByteArrayContent content = new(Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType) { CharSet = "utf-8" };
        }

        using HttpRequestMessage request = new(HttpMethod.Post, McpTestHost.Path)
        {
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return await client.SendAsync(request);
    }
}
