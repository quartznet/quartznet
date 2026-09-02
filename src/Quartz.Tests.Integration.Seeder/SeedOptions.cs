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

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// The command line, parsed.
/// </summary>
internal sealed class SeedOptions
{
    public string Dialect { get; private set; } = "";

    public string ConnectionString { get; private set; } = "";

    public string TablePrefix { get; private set; } = "QRTZU_";

    /// <summary><c>json</c> for Newtonsoft, <c>stj</c> for System.Text.Json.</summary>
    public string Serializer { get; private set; } = "";

    public string SchedulerName { get; private set; } = "Quartz320Upgrade";

    public string InstanceId { get; private set; } = "seed-node";

    /// <summary>
    /// A fresh-install script to run before seeding. Optional: the rehearsal test creates the schema
    /// itself, through the database's own command-line client, because that is what understands each
    /// dialect's batch separator. Passing it here is for running the seeder by hand.
    /// </summary>
    public string? SchemaScript { get; private set; }

    /// <summary>Where <c>seed.json</c> goes.</summary>
    public string OutputDirectory { get; private set; } = "";

    /// <summary>Where the blob-column dumps go, if anywhere.</summary>
    public string? FixtureDirectory { get; private set; }

    public static SeedOptions Parse(string[] args)
    {
        SeedOptions options = new SeedOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{name}'.");
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"'{name}' needs a value.");
            }

            string value = args[++i];

            switch (name)
            {
                case "--dialect":
                    options.Dialect = value;
                    break;
                case "--connection-string":
                    options.ConnectionString = value;
                    break;
                case "--table-prefix":
                    options.TablePrefix = value;
                    break;
                case "--serializer":
                    options.Serializer = value;
                    break;
                case "--scheduler-name":
                    options.SchedulerName = value;
                    break;
                case "--instance-id":
                    options.InstanceId = value;
                    break;
                case "--schema":
                    options.SchemaScript = value;
                    break;
                case "--output":
                    options.OutputDirectory = value;
                    break;
                case "--fixture-output":
                    options.FixtureDirectory = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{name}'.");
            }
        }

        Require(options.Dialect, "--dialect");
        Require(options.ConnectionString, "--connection-string");
        Require(options.Serializer, "--serializer");
        Require(options.OutputDirectory, "--output");

        if (options.Serializer is not ("json" or "stj"))
        {
            throw new ArgumentException("--serializer has to be 'json' (Newtonsoft) or 'stj' (System.Text.Json).");
        }

        return options;
    }

    /// <summary>The folder name a serializer's fixtures are dumped under.</summary>
    public string SerializerFolder => Serializer == "json" ? "newtonsoft" : "stj";

    private static void Require(string value, string name)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"{name} is required.");
        }
    }

    public const string Usage = """
        Quartz.Tests.Integration.Seeder -- fills a 3.20 schema with rows a released Quartz 3.20.0 wrote.

          --dialect            sqlite | sqlServer | postgres | mysql_innodb | oracle | firebird
          --connection-string  the connection string for that database
          --serializer         json (Newtonsoft) | stj (System.Text.Json)
          --output             directory to write seed.json to
          --table-prefix       table prefix to seed under            (default QRTZU_)
          --scheduler-name     scheduler instance name               (default Quartz320Upgrade)
          --instance-id        scheduler instance id                 (default seed-node)
          --schema             fresh-install script to run first     (optional)
          --fixture-output     directory to dump the blob columns to (optional)
        """;
}
