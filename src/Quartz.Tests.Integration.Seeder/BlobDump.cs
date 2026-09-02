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

using System.Data.Common;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// Writes the four blob columns out as files, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// These are the fixtures <c>LegacyJsonPayloadTest</c> reads. Byte for byte matters: the point of
/// capturing them is that "3.x wrote this" becomes a fact about bytes a released 3.20 process
/// produced, rather than a comment above a hand-transcribed literal. So nothing here reformats,
/// re-indents or re-encodes — what the column holds is what the file holds.
/// </para>
/// <para>
/// Both serializers are captured, into <c>newtonsoft/</c> and <c>stj/</c>, because they write
/// genuinely different shapes: with 3.x's default settings the Newtonsoft one writes a trigger as a
/// plain object graph carrying <c>$type</c>, and the System.Text.Json one writes the discriminated
/// <c>TriggerType</c> form.
/// </para>
/// </remarks>
internal static class BlobDump
{
    public static List<string> Write(DbConnection connection, SeedOptions options)
    {
        string directory = Path.Combine(options.FixtureDirectory!, options.SerializerFolder);
        Directory.CreateDirectory(directory);

        string prefix = options.TablePrefix;
        string scheduler = LegacySeeder.Literal(options.SchedulerName);
        List<string> written = [];

        Dump(connection, directory, written, "job-data-map.json",
            $"SELECT JOB_DATA FROM {prefix}JOB_DETAILS WHERE SCHED_NAME = {scheduler} "
            + $"AND JOB_NAME = {LegacySeeder.Literal(LegacySeeder.WorkerJobName)} "
            + $"AND JOB_GROUP = {LegacySeeder.Literal(LegacySeeder.JobGroup)}");

        Dump(connection, directory, written, "trigger-job-data-map.json",
            $"SELECT JOB_DATA FROM {prefix}TRIGGERS WHERE SCHED_NAME = {scheduler} "
            + $"AND TRIGGER_NAME = 'simple' AND TRIGGER_GROUP = {LegacySeeder.Literal(LegacySeeder.TriggerGroup)}");

        foreach (string calendar in new[] { "annual", "holiday", "monthly", "weekly", "daily", "cron", "chained" })
        {
            Dump(connection, directory, written, $"calendar-{calendar}.json",
                $"SELECT CALENDAR FROM {prefix}CALENDARS WHERE SCHED_NAME = {scheduler} "
                + $"AND CALENDAR_NAME = {LegacySeeder.Literal(calendar)}");
        }

        foreach (string trigger in BlobStorageOverride.Families(options.Serializer))
        {
            Dump(connection, directory, written, $"trigger-{trigger}.json",
                $"SELECT BLOB_DATA FROM {prefix}BLOB_TRIGGERS WHERE SCHED_NAME = {scheduler} "
                + $"AND TRIGGER_NAME = {LegacySeeder.Literal(trigger)} "
                + $"AND TRIGGER_GROUP = {LegacySeeder.Literal(BlobStorageOverride.Group)}");
        }

        written.Sort(StringComparer.Ordinal);
        return written;
    }

    private static void Dump(DbConnection connection, string directory, List<string> written, string fileName, string sql)
    {
        byte[] payload = Read(connection, sql)
            ?? throw new InvalidOperationException($"No blob to dump for {fileName}; the row is missing or its column is null.");

        File.WriteAllBytes(Path.Combine(directory, fileName), payload);
        written.Add(fileName);
    }

    private static byte[]? Read(DbConnection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        using DbDataReader reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0))
        {
            return null;
        }

        object value = reader.GetValue(0);
        if (value is byte[] bytes)
        {
            return bytes;
        }

        // Oracle hands back its own LOB type rather than a byte array.
        using MemoryStream buffer = new MemoryStream();
        using Stream stream = reader.GetStream(0);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
