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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// A host newer than this client, as far as one answer goes: a trigger listing carrying names no version
/// of Quartz has.
/// </summary>
/// <remarks>
/// <para>
/// The answer is canned rather than served by a scheduler, because no scheduler of this version can be in
/// a state it has no member for. It is served beside the real API, under its own path, and read by a client
/// of its own through the same <c>AddQuartzHttpClient</c> registration — so what runs is the client's
/// tolerant reading, out of generated metadata, with reflection off.
/// </para>
/// <para>
/// One trigger is in a state this client cannot name, and is left out. The other has an overlap policy it
/// cannot name, which reads as <see cref="OverlapPolicy.Default" />.
/// </para>
/// </remarks>
internal static class NewerHost
{
    /// <summary>
    /// Where the canned answers are served, relative to the site root, ending in <c>/</c> as a client's base
    /// address has to.
    /// </summary>
    internal const string Path = "newer-host/";

    private const string Triggers = """
        {
          "items": [
            {
              "name": "parked", "group": "canary", "jobName": "wire", "jobGroup": "canary", "triggerType": "Cron",
              "state": "Hibernating", "startTimeUtc": "2099-01-01T12:00:00+00:00", "priority": 5, "retryAttempt": 0
            },
            {
              "name": "known", "group": "canary", "jobName": "wire", "jobGroup": "canary", "triggerType": "Cron",
              "state": "Normal", "startTimeUtc": "2099-01-01T12:00:00+00:00", "priority": 5, "retryAttempt": 0,
              "overlapPolicy": "Staggered", "leaseOwner": "node-z"
            }
          ],
          "hasMore": false,
          "totalCount": 2
        }
        """;

    /// <summary>
    /// Serves the canned listing. A plain <see cref="RequestDelegate" />, so the route needs nothing
    /// generated and nothing reflected.
    /// </summary>
    public static void Map(WebApplication app)
    {
        RequestDelegate triggers = static context =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(Triggers, context.RequestAborted);
        };

        app.MapGet($"/{Path}schedulers/{{schedulerName}}/triggers", triggers);
    }
}
