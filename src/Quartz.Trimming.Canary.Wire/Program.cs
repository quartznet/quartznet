#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The HTTP API's wire path out of a trimmed or natively compiled publish: <c>Quartz.AspNetCore</c>
/// serving a scheduler over Kestrel, and <c>Quartz.HttpClient</c> driving it over a loopback socket, in
/// one process.
/// </summary>
/// <remarks>
/// <para>
/// The two halves share the socket and nothing else. The host has a container with the scheduler in it;
/// the client has one of its own, holding nothing but <c>AddQuartzHttpClient</c>. So every step in
/// <see cref="WireCheck" /> goes through a request delegate the source generator wrote, through the wire
/// contract's generated metadata on both sides, and through HTTP. The last step is the exception: its
/// answer is the canned one <see cref="NewerHost" /> serves, read by a second client.
/// </para>
/// <para>
/// One line per step, and a non-zero exit code when anything failed.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The scheduler's name. The client is registered under it too, because a request names the
    /// scheduler it is for.
    /// </summary>
    internal const string SchedulerName = "WireCanary";

    public static async Task<int> Main()
    {
        Console.WriteLine($"IsReflectionEnabledByDefault: {JsonSerializer.IsReflectionEnabledByDefault}");
        Console.WriteLine($"IsDynamicCodeSupported: {RuntimeFeature.IsDynamicCodeSupported}");

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            Console.WriteLine("FAIL reflection-is-off: JsonSerializer.IsReflectionEnabledByDefault is true, so this run proves nothing. Publish with PublishTrimmed=true.");
            return 1;
        }

        // A bound on the whole run, so a step that hangs ends the canary rather than the CI job around it.
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5), TimeProvider.System);

        bool passed;
        try
        {
            passed = await Run(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Console.WriteLine($"FAIL host: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}");
            passed = false;
        }

        Console.WriteLine(passed
            ? "Quartz.Trimming.Canary.Wire: a scheduler served by Quartz.AspNetCore is scheduled, read, paused, resumed, triggered and asked for its history through Quartz.HttpClient, which also reads a newer host's names."
            : "Quartz.Trimming.Canary.Wire: a step failed.");

        return passed ? 0 : 1;
    }

    private static async Task<bool> Run(CancellationToken cancellationToken)
    {
        WebApplication host = BuildHost();
        await using ConfiguredAsyncDisposable hostDisposal = host.ConfigureAwait(false);

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        Uri apiAddress = ApiAddress(host);
        Console.WriteLine($"PASS host: Quartz.AspNetCore serves '{SchedulerName}' at {apiAddress}");

        using HttpClient httpClient = new() { BaseAddress = apiAddress };

        ServiceProvider client = BuildClient(httpClient);
        await using ConfiguredAsyncDisposable clientDisposal = client.ConfigureAwait(false);

        using HttpClient newerHostClient = new() { BaseAddress = new Uri(apiAddress, $"../{NewerHost.Path}") };

        ServiceProvider newerHost = BuildClient(newerHostClient);
        await using ConfiguredAsyncDisposable newerHostDisposal = newerHost.ConfigureAwait(false);

        bool passed = await WireCheck.Run(client, newerHost, cancellationToken).ConfigureAwait(false);

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
        return passed;
    }

    /// <summary>
    /// The server half: a minimal-API host with a scheduler over the in-memory store and the HTTP API
    /// mapped.
    /// </summary>
    private static WebApplication BuildHost()
    {
        // Rooted beside the executable rather than wherever it was started from, so the host watches no
        // directory it has no business in. CI starts it from the repository root.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        // A port the operating system picks, on loopback. The only caller is this process.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Nothing on the console but the steps. A failure still says why: the problem details below carry
        // the server's stack trace, and the client's exception carries the problem details.
        builder.Logging.ClearProviders();

        builder.Services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = SchedulerName);

            // What the trimming how-to tells a host to do for a job type a caller may name: register it.
            // The request carries the type as a string, and this is what keeps the type for it to resolve to.
            quartz.AddJobType<WireCanaryJob>();

            // So the history route that reads one execution has a captured log to carry back.
            quartz.UseExecutionLogCapture();
        });

        // Started inside StartAsync rather than once the application has started. The client's first
        // request follows StartAsync at once, and would otherwise race the scheduler it asks about.
        builder.Services.AddQuartzHostedService(options => options.AwaitApplicationStarted = false);

        builder.Services.AddQuartzHttpApi(options => options.IncludeStackTraceInProblemDetails = true);

        WebApplication app = builder.Build();

        // The API refuses to start without an authorization decision, and this is one: the only caller is
        // this process, over loopback.
        app.MapQuartzHttpApi().AllowAnonymous();

        NewerHost.Map(app);

        return app;
    }

    /// <summary>
    /// Where the API is being served, read back from Kestrel once it has bound its port.
    /// </summary>
    private static Uri ApiAddress(WebApplication host)
    {
        IServerAddressesFeature addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no addresses.");

        // The site root plus the API path, ending in '/': the client resolves every route against it.
        return new Uri($"{addresses.Addresses.Single()}/quartz-api/");
    }

    /// <summary>
    /// The client half: a container of its own, as a remote caller's would be.
    /// </summary>
    private static ServiceProvider BuildClient(HttpClient httpClient)
    {
        ServiceCollection services = new();

        // Handed over rather than built by IHttpClientFactory, because the address is only known once
        // Kestrel has bound its port.
        services.AddQuartzHttpClient(SchedulerName, _ => httpClient);

        return services.BuildServiceProvider();
    }
}
