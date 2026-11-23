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

using Microsoft.EntityFrameworkCore.Migrations;

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// The statements that give a schema EF Core created the parts its model cannot say.
/// </summary>
internal static class QuartzSchemaCompletion
{
    internal static void Add(MigrationBuilder migrationBuilder, QuartzDialect dialect, QuartzTableNaming naming)
    {
        foreach (QuartzTriggerDefinition trigger in dialect.DeleteTriggers)
        {
            migrationBuilder.Sql(string.Format(CultureInfo.InvariantCulture, trigger.Sql, naming.TablePrefix));
        }

        if (!dialect.RebuildsDescendingIndexes)
        {
            return;
        }

        foreach (QuartzTableDefinition table in dialect.Tables)
        {
            foreach (QuartzIndexDefinition index in table.Indexes.Where(i => i.Descending is not null))
            {
                // One statement, so the table is never without the index; the same index whether or not the
                // provider wrote the direction the first time.
                string name = MySqlIdentifier(naming.Index(index.Suffix));
                string columns = string.Join(", ", index.Columns.Select((column, i) =>
                    MySqlIdentifier(naming.Column(column)) + (index.Descending![i] ? " DESC" : "")));

                migrationBuilder.Sql(
                    $"ALTER TABLE {MySqlIdentifier(naming.Table(table.Name))} DROP INDEX {name}, ADD INDEX {name} ({columns});");
            }
        }
    }

    private static string MySqlIdentifier(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
}
