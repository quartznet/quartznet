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

using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Impl;

/// <summary>
/// Reads the events of a scheduler in another process through the Quartz HTTP API's event route.
/// </summary>
/// <remarks>
/// <para>
/// One enumeration, however many connections it takes. A stream that drops is reopened after a delay that
/// doubles from a second to half a minute, so a reader — the dashboard's Live Logs page, or code of your
/// own — sees one unbroken sequence rather than having to reconnect itself. Nothing is replayed across a
/// reconnection: the route serves no history, so the events of the gap are gone and a reader that needs
/// them asks the history routes.
/// </para>
/// <para>
/// The heartbeats the route emits are consumed here. They exist so that a reader can tell a quiet
/// scheduler from a dead connection, which is this class's question rather than its caller's.
/// </para>
/// <para>
/// An enumeration is pulled, so everything above happens while a subscriber is asking for the next event:
/// a dropped connection is noticed on the read that was waiting for it, and a subscriber that has stopped
/// reading has stopped reconnecting too. A page that reads in a loop — which is what a live view is —
/// never sees the difference.
/// </para>
/// <para>
/// A target whose API predates the route answers <c>404</c> with no problem details, reported as
/// <see cref="NotSupportedException" /> — "this target serves no event stream" is a fact a caller can
/// render, where an exception about a missing route is not. It is the rule
/// <see cref="HttpExecutionHistoryStore" /> applies to the history routes, and it is not confused with the
/// <c>404</c> that names an unknown scheduler: that one carries problem details, arrives as
/// <see cref="HttpClientException" />, and is not something to reconnect over.
/// </para>
/// </remarks>
internal sealed class HttpSchedulerEventReader : ISchedulerEventSource
{
    /// <summary>
    /// How long to wait before reopening a stream that dropped, and the most that wait grows to.
    /// </summary>
    /// <remarks>
    /// A second, doubling to thirty. Short enough that a restarted worker is picked up while an operator
    /// is still looking at the page, and bounded so that an unreachable target is asked twice a minute
    /// rather than continuously.
    /// </remarks>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    /// <inheritdoc cref="FirstRetryDelay" />
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly string schedulerName;
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly TimeProvider timeProvider;

    /// <param name="schedulerName">The remote scheduler's name, which the route is addressed to.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">
    /// Optional serializer options. A copy is taken and Quartz's own converters are added to the copy, so
    /// the instance passed in is left untouched.
    /// </param>
    /// <param name="timeProvider">The clock the delay between reconnections is measured on.</param>
    public HttpSchedulerEventReader(
        string schedulerName,
        HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(httpClient);

        this.schedulerName = schedulerName;
        this.httpClient = httpClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;

        this.jsonSerializerOptions = jsonSerializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(jsonSerializerOptions);

        this.jsonSerializerOptions.ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
    }

    /// <inheritdoc />
    /// <remarks>
    /// The name in the route is this reader's own rather than the argument's: one registration is one
    /// remote scheduler, and a caller subscribing to another scheduler's events here is asking the wrong
    /// target.
    /// </remarks>
    public async IAsyncEnumerable<SchedulerEvent> Subscribe(
        string schedulerName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        TimeSpan retryIn = FirstRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            Connection? connection = await Open(cancellationToken).ConfigureAwait(false);

            if (connection is not null)
            {
                while (true)
                {
                    SchedulerEvent? read = await connection.Next().ConfigureAwait(false);
                    if (read is null)
                    {
                        break;
                    }

                    // A connection that delivered something is a healthy one, whatever the last one did,
                    // so the next drop waits a second rather than however long the last outage reached.
                    retryIn = FirstRetryDelay;
                    yield return read;
                }

                await connection.DisposeAsync().ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested || !await Wait(retryIn, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            retryIn = retryIn >= MaxRetryDelay ? MaxRetryDelay : Shorter(retryIn + retryIn, MaxRetryDelay);
        }
    }

    /// <summary>
    /// Opens the stream, or answers <see langword="null" /> when the target could not be reached and the
    /// subscription should try again.
    /// </summary>
    /// <remarks>
    /// The failures that are worth retrying are the ones that say nothing about the request: a refused
    /// connection, a socket that went away, a gateway between here and there, and the client's own
    /// timeout. Everything else is reported to the caller — an unknown scheduler, a caller the target's
    /// policy refuses, and a target that serves no event stream at all are answers rather than outages,
    /// and reconnecting over them would ask the same refused question every thirty seconds for ever.
    /// </remarks>
    private async ValueTask<Connection?> Open(CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response = await httpClient
                .GetStream($"schedulers/{schedulerName}/events", jsonSerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return await Connection.Read(response, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotSupportedException(
                $"The scheduler '{schedulerName}' is reached over HTTP and the target serves no event stream: "
                + "it answered 404 for the events route, which a Quartz HTTP API older than 4.1 does. Upgrade the "
                + "scheduler's host to stream its events.",
                exception);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            // The client's own timeout rather than this subscription's, which the guard above would have
            // caught. A target that did not answer in time is one to ask again.
            return null;
        }
    }

    /// <summary>
    /// Waits before reopening the stream, answering <see langword="false" /> when the subscription was
    /// cancelled while waiting — which ends the enumeration rather than raising, as the contract says.
    /// </summary>
    private async ValueTask<bool> Wait(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static TimeSpan Shorter(TimeSpan left, TimeSpan right) => left < right ? left : right;

    /// <summary>
    /// One open stream, read frame by frame.
    /// </summary>
    /// <remarks>
    /// The response, the body and the parser's enumerator belong together: all three are disposed when the
    /// stream ends, and the enumerator cannot outlive the response it reads. A failure while reading is
    /// the end of this connection rather than of the subscription, so <see cref="Next" /> reports it as
    /// "no more events" and the caller reopens.
    /// </remarks>
    private sealed class Connection : IAsyncDisposable
    {
        private readonly HttpResponseMessage response;
        private readonly Stream body;
        private readonly IAsyncEnumerator<SseItem<SchedulerEvent?>> frames;
        private readonly CancellationToken cancellationToken;

        private Connection(
            HttpResponseMessage response,
            Stream body,
            IAsyncEnumerator<SseItem<SchedulerEvent?>> frames,
            CancellationToken cancellationToken)
        {
            this.response = response;
            this.body = body;
            this.frames = frames;
            this.cancellationToken = cancellationToken;
        }

        public static async ValueTask<Connection> Read(
            HttpResponseMessage response,
            JsonSerializerOptions jsonSerializerOptions,
            CancellationToken cancellationToken)
        {
            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // The generated metadata for the event, which is what keeps the parse trimmable: the frame's
            // bytes are the same JSON the API writes every other body as.
            SseParser<SchedulerEvent?> parser = SseParser.Create(
                body,
                (_, data) => JsonSerializer.Deserialize(data, HttpClientExtensions.WireFormatOf<SchedulerEvent>(jsonSerializerOptions)));

            return new Connection(
                response,
                body,
                parser.EnumerateAsync(cancellationToken).GetAsyncEnumerator(CancellationToken.None),
                cancellationToken);
        }

        /// <summary>
        /// The next event this connection carries, or <see langword="null" /> when it has none left —
        /// because the stream ended, because it broke, or because the subscription was cancelled.
        /// </summary>
        public async ValueTask<SchedulerEvent?> Next()
        {
            while (true)
            {
                try
                {
                    if (!await frames.MoveNextAsync().ConfigureAwait(false))
                    {
                        return null;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (HttpRequestException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }

                SchedulerEvent? read = frames.Current.Data;

                // A heartbeat is the route saying the connection is alive, which is what this class reads
                // it for. A frame whose body could not be read at all is dropped the same way: one
                // unreadable event is not a reason to tear a live view down.
                if (read is not null && read.Kind != SchedulerEventKind.Heartbeat)
                {
                    return read;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await frames.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                // Disposing a stream that has already broken is not a failure of anything.
            }

            await body.DisposeAsync().ConfigureAwait(false);
            response.Dispose();
        }
    }
}
