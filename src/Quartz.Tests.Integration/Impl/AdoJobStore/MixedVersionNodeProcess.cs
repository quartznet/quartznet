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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// One <c>Quartz.Tests.Integration.MixedVersionNode</c> process, and the line protocol that drives it.
/// </summary>
/// <remarks>
/// <para>
/// A node is a process of its own because a released Quartz and this repository's have the same
/// assembly identity, so no one process can load both. Each command is a line on the node's standard
/// input and gets one <c>@@</c> reply on its standard output; everything else the node prints is kept as
/// its log, for the failure messages. <c>Protocol.cs</c> in the node's project describes the protocol.
/// </para>
/// <para>
/// Disposing stops the process whatever state it is in: standard input is closed, which the node takes
/// as a shutdown, and a node that has not exited after that is killed — this process's own child, by
/// its <see cref="Process" /> handle, never by name.
/// </para>
/// </remarks>
internal sealed class MixedVersionNodeProcess : IAsyncDisposable
{
    private const string ReplyPrefix = "@@";
    private const int LogLines = 400;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMinutes(2);

    private readonly Process process;
    private readonly Channel<string> replies = Channel.CreateUnbounded<string>();
    private readonly ConcurrentQueue<string> log = new();
    private readonly Task pumps;

    private MixedVersionNodeProcess(string instanceId, Process process)
    {
        InstanceId = instanceId;
        this.process = process;
        pumps = Task.WhenAll(PumpOutput(), PumpErrors());
    }

    public string InstanceId { get; }

    /// <summary>The informational version of the Quartz assembly the node loaded.</summary>
    public string QuartzVersion { get; private set; } = "";

    /// <summary><c>released</c> or <c>working-tree</c>, as the node was compiled.</summary>
    public string Build { get; private set; } = "";

    public static async Task<MixedVersionNodeProcess> Start(
        string assembly,
        string instanceId,
        string schedulerName,
        string connectionString,
        string tablePrefix,
        string runsTable,
        bool history)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--instance-id");
        start.ArgumentList.Add(instanceId);
        start.ArgumentList.Add("--scheduler-name");
        start.ArgumentList.Add(schedulerName);
        start.ArgumentList.Add("--connection-string");
        start.ArgumentList.Add(connectionString);
        start.ArgumentList.Add("--table-prefix");
        start.ArgumentList.Add(tablePrefix);
        start.ArgumentList.Add("--runs-table");
        start.ArgumentList.Add(runsTable);

        if (history)
        {
            start.ArgumentList.Add("--history");
        }

        MixedVersionNodeProcess node = new(instanceId, Process.Start(start)!);
        try
        {
            (string kind, Dictionary<string, string> values) = await node.NextReply("starting");
            kind.Should().Be("ready", $"a node says it is ready once its scheduler is built. {node.Describe()}");

            node.QuartzVersion = values["quartz"];
            node.Build = values["build"];
            values["instance"].Should().Be(instanceId);
            return node;
        }
        catch
        {
            await node.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Sends one command and returns what the node answered, failing with its log when it refused.
    /// </summary>
    public async Task<Dictionary<string, string>> Send(string verb, params (string Key, object Value)[] arguments)
    {
        StringBuilder line = new(verb);
        foreach ((string key, object value) in arguments)
        {
            line.Append(' ').Append(key).Append('=').Append(Uri.EscapeDataString(Format(value)));
        }

        await process.StandardInput.WriteLineAsync(line.ToString());
        await process.StandardInput.FlushAsync();

        (string kind, Dictionary<string, string> values) = await NextReply(verb);
        if (kind != "ok")
        {
            throw new InvalidOperationException(
                $"{InstanceId} could not do '{line}': {values.GetValueOrDefault("message")}{Environment.NewLine}{Describe()}");
        }

        return values;
    }

    /// <summary>
    /// Shuts the scheduler down and waits for the process to exit.
    /// </summary>
    /// <param name="waitForJobs">
    /// Whether running jobs finish first, which is what makes every execution the node started one the
    /// runs table has an end for.
    /// </param>
    public async Task Stop(bool waitForJobs)
    {
        await Send("shutdown", ("wait", waitForJobs));

        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.Should().Be(0, $"a node that was told to shut down exits cleanly. {Describe()}");
    }

    /// <summary>
    /// The node's Quartz and the last of what it logged, for a failure message.
    /// </summary>
    public string Describe()
    {
        string[] lines = log.ToArray();
        string exit = process.HasExited ? $", exited {process.ExitCode.ToString(CultureInfo.InvariantCulture)}" : "";
        return $"{InstanceId} (Quartz {QuartzVersion}, {Build}{exit}) logged {lines.Length} line(s)"
               + (lines.Length == 0 ? "." : ":" + Environment.NewLine + string.Join(Environment.NewLine, lines.TakeLast(60)));
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            try
            {
                // The node takes the end of its input as a shutdown that does not wait for jobs.
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Already gone.
            }

            using CancellationTokenSource grace = new(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(grace.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        await pumps;
        process.Dispose();
    }

    private async Task<(string Kind, Dictionary<string, string> Values)> NextReply(string waitingFor)
    {
        using CancellationTokenSource timeout = new(ReplyTimeout);

        string line;
        try
        {
            line = await replies.Reader.ReadAsync(timeout.Token);
        }
        catch (ChannelClosedException)
        {
            await process.WaitForExitAsync();
            throw new InvalidOperationException($"{InstanceId} exited while it was {waitingFor}. {Describe()}");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"{InstanceId} gave no answer to '{waitingFor}' in {ReplyTimeout.TotalSeconds:0} s. {Describe()}");
        }

        string[] parts = line[ReplyPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string part in parts.Skip(1))
        {
            int separator = part.IndexOf('=', StringComparison.Ordinal);
            values[part[..separator]] = Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        return (parts[0], values);
    }

    private async Task PumpOutput()
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(ReplyPrefix, StringComparison.Ordinal))
            {
                replies.Writer.TryWrite(line);
            }
            else
            {
                Keep(line);
            }
        }

        replies.Writer.TryComplete();
    }

    private async Task PumpErrors()
    {
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            Keep(line);
        }
    }

    private void Keep(string line)
    {
        log.Enqueue(line);
        while (log.Count > LogLines && log.TryDequeue(out _))
        {
        }
    }

    private static string Format(object value) => value switch
    {
        DateTimeOffset instant => instant.UtcTicks.ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
