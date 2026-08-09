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

using System.Text.Json;

namespace Quartz;

public class HttpClientOptions
{
    /// <summary>
    /// Name of the scheduler, must be same as the remote scheduler.
    /// </summary>
    public string SchedulerName { get; set; } = null!;

    /// <summary>
    /// If given, IHttpClientFactory is used to fetch HttpClient with this name.
    /// </summary>
    /// <remarks>
    /// Either this or HttpClient must be given
    /// </remarks>
    public string? HttpClientName { get; set; }

    /// <summary>
    /// If given this HttpClient will be used
    /// </summary>
    /// <remarks>
    /// Either this or HttpClientName must be given
    /// </remarks>
    public System.Net.Http.HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Optional json serializer options to be used by the HTTP scheduler
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    internal void AssertValid()
    {
        if (string.IsNullOrWhiteSpace(SchedulerName))
        {
            throw new InvalidOperationException("Scheduler name required");
        }

        if (string.IsNullOrWhiteSpace(HttpClientName) && HttpClient is null)
        {
            throw new InvalidOperationException($"Either {nameof(HttpClientName)} or {nameof(HttpClient)} instance is required");
        }

        if (!string.IsNullOrWhiteSpace(HttpClientName) && HttpClient is not null)
        {
            throw new InvalidOperationException($"Both {nameof(HttpClientName)} and {nameof(HttpClient)} instance have been set, only one can be set");
        }
    }
}