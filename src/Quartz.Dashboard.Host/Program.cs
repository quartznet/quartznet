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

namespace Quartz.Dashboard.Host;

/// <summary>
/// The entry point of the tool and the container image.
/// </summary>
/// <remarks>
/// A class in the host's own namespace rather than top-level statements, whose <c>Program</c> is global: the
/// tests stand this up with <c>WebApplicationFactory&lt;Program&gt;</c> beside another application's
/// <c>Program</c>, and the namespace is what tells the two apart.
/// </remarks>
internal sealed class Program
{
    private Program()
    {
    }

    public static int Main(string[] args)
    {
        // The dashboard's assets are published beside the host, which for the tool is not where it was
        // started from: the web root is the host's own, and the content root - where appsettings.json is
        // read from - stays the working directory, so `quartz-dashboard` reads the appsettings.json of the
        // directory it is run in.
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });

        DashboardHostPlan plan = builder.AddDashboardHost();

        WebApplication app = builder.Build();

        app.UseDashboardHost(plan);

        try
        {
            app.Run();
        }
        catch (DashboardHostRefusedException)
        {
            // Said already, as event 9402 per problem. An exit code rather than an unhandled exception: in a
            // container the runtime's abort on one reads as a crash.
            return 1;
        }

        return 0;
    }
}
