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

using System.Text;

using Quartz.Tests.Integration.Seeder;

using JsonSerializer = System.Text.Json.JsonSerializer;

if (args.Length == 0)
{
    Console.WriteLine(SeedOptions.Usage);
    return 1;
}

SeedOptions options;
try
{
    options = SeedOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(SeedOptions.Usage);
    return 2;
}

try
{
    SeedManifest manifest = await new LegacySeeder(options).RunAsync().ConfigureAwait(false);

    Directory.CreateDirectory(options.OutputDirectory);
    string path = Path.Combine(options.OutputDirectory, "seed.json");

    // UTF-8 with no byte order mark, like every other file this repository writes.
    File.WriteAllText(path, JsonSerializer.Serialize(manifest, SeedManifest.SerializerOptions), new UTF8Encoding(false));

    Console.WriteLine($"Seeded {options.Dialect} under {options.TablePrefix} with the {options.SerializerFolder} serializer; manifest at {path}.");
    Console.Out.Flush();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.Flush();

    // Exit rather than return: 3.x's scheduler thread is a foreground one, so a plain return from a
    // run that got as far as Start() would report the failure and then hang forever holding it.
    Environment.Exit(3);
}

// Killed rather than shut down, on purpose: one firing is still in flight, and the
// QRTZ_FIRED_TRIGGERS row it left is what the rehearsal recovers. A clean shutdown would tidy it away.
Environment.Exit(0);
return 0;
