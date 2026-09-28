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

using System.Globalization;

namespace Quartz.Tests.Integration.MixedVersionNode;

/// <summary>
/// The line protocol between a node and the test driving it.
/// </summary>
/// <remarks>
/// <para>
/// One command a line on standard input: a verb, then <c>key=value</c> pairs whose values are
/// URI-escaped, so a value can hold a space. One reply a command on standard output, prefixed
/// <c>@@</c> so that nothing else a process might print is mistaken for one: <c>@@ok</c> with the
/// command's results as pairs of the same shape, or <c>@@error</c> with the failure on one line.
/// </para>
/// <para>
/// A node that starts says <c>@@ready</c> with the Quartz it runs; standard input closing is the same
/// as <c>shutdown</c> without waiting for jobs, so a test process that dies takes its nodes with it.
/// </para>
/// </remarks>
internal static class Protocol
{
    public const string Prefix = "@@";
    public const string Ready = "ready";
    public const string Ok = "ok";
    public const string Error = "error";

    private static readonly Lock Gate = new();

    public static void Reply(string kind, IEnumerable<KeyValuePair<string, string>> values)
    {
        string line = Prefix + kind + string.Concat(values.Select(x => " " + x.Key + "=" + Uri.EscapeDataString(x.Value)));
        Write(line);
    }

    public static void Reply(string kind, Exception exception)
    {
        Reply(kind, [new KeyValuePair<string, string>("message", exception.GetType().Name + ": " + exception.Message)]);
        Console.Error.WriteLine(exception);
    }

    public static Command Parse(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> arguments = new(StringComparer.Ordinal);
        foreach (string part in parts.Skip(1))
        {
            int separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new ArgumentException($"'{part}' is not a key=value pair.");
            }

            arguments[part[..separator]] = Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        return new Command(parts.Length > 0 ? parts[0] : "", arguments);
    }

    private static void Write(string line)
    {
        lock (Gate)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }
}

/// <summary>
/// One command, and typed access to its arguments.
/// </summary>
internal sealed record Command(string Verb, Dictionary<string, string> Arguments)
{
    public string Text(string key) => Arguments.TryGetValue(key, out string? value)
        ? value
        : throw new ArgumentException($"'{Verb}' needs {key}=.");

    public string? OptionalText(string key) => Arguments.GetValueOrDefault(key);

    public int Number(string key) => int.Parse(Text(key), CultureInfo.InvariantCulture);

    public bool Flag(string key) => bool.Parse(Text(key));

    /// <summary>An instant, sent as UTC ticks so that no format stands between the two processes.</summary>
    public DateTimeOffset Instant(string key) => new(long.Parse(Text(key), CultureInfo.InvariantCulture), TimeSpan.Zero);
}
