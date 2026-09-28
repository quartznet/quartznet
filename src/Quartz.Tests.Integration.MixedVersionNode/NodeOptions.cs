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

using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Integration.MixedVersionNode;

/// <summary>
/// What one node is: its place in the cluster, the database it shares, and the table its jobs record to.
/// </summary>
internal sealed class NodeOptions
{
    public const string Usage =
        """
        --instance-id        this node's instance id
        --scheduler-name     the cluster's scheduler name
        --connection-string  the PostgreSQL both nodes share
        --table-prefix       the Quartz table prefix
        --runs-table         the table every job execution is recorded in
        --log-level          the least level written to standard error (default Warning)
        """;

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public required string InstanceId { get; init; }

    public required string SchedulerName { get; init; }

    public required string ConnectionString { get; init; }

    public required string TablePrefix { get; init; }

    public required string RunsTable { get; init; }

    public LogLevel LogLevel { get; init; } = LogLevel.Warning;

    public static NodeOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value' pairs, got '{args[i]}'.");
            }

            values[args[i][2..]] = args[i + 1];
        }

        string Required(string name) => values.TryGetValue(name, out string? value) && value.Length > 0
            ? value
            : throw new ArgumentException($"--{name} is required.");

        NodeOptions options = new()
        {
            InstanceId = Required("instance-id"),
            SchedulerName = Required("scheduler-name"),
            ConnectionString = Required("connection-string"),
            TablePrefix = Required("table-prefix"),
            RunsTable = Required("runs-table"),
            LogLevel = values.TryGetValue("log-level", out string? level) ? Enum.Parse<LogLevel>(level, ignoreCase: true) : LogLevel.Warning
        };

        // Both names are written into SQL text, so they are held to what an unquoted identifier may be.
        foreach (string identifier in (string[]) [options.TablePrefix, options.RunsTable])
        {
            if (!Identifier.IsMatch(identifier))
            {
                throw new ArgumentException($"'{identifier}' is not a plain SQL identifier.");
            }
        }

        return options;
    }
}
