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
using System.Text;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// Runs a fresh-install script through an ordinary ADO connection.
/// </summary>
/// <remarks>
/// <para>
/// Only SQLite is supported, and deliberately. Every other dialect's script is written for that
/// database's own command-line client — <c>GO</c>, <c>/</c>, <c>SET TERM</c> — and the rehearsal test
/// runs it through exactly that client inside the container, which is the only way to be sure the
/// script a user is told to run is the script that ran. Splitting those here would be a second,
/// worse copy of a thing that already works.
/// </para>
/// <para>
/// SQLite has no such client and no container, so the seeder does the splitting itself: statements
/// end at a semicolon, except inside the <c>BEGIN … END</c> body of a referential-integrity trigger,
/// which is the one construct the script uses.
/// </para>
/// </remarks>
internal static class SchemaScript
{
    public static void Apply(DbConnection connection, string dialect, string script)
    {
        if (dialect != "sqlite")
        {
            throw new NotSupportedException(
                $"The seeder can only create a schema for sqlite; for {dialect} create it first and leave --schema out.");
        }

        foreach (string statement in SplitSqlite(script))
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static List<string> SplitSqlite(string script)
    {
        List<string> statements = [];
        StringBuilder current = new StringBuilder();
        int depth = 0;

        foreach (string line in script.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            current.AppendLine(line);

            if (trimmed.EndsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                continue;
            }

            if (trimmed.StartsWith("END", StringComparison.OrdinalIgnoreCase) && depth > 0)
            {
                depth--;
            }

            if (depth == 0 && trimmed.EndsWith(';'))
            {
                Take(statements, current);
            }
        }

        Take(statements, current);
        return statements;
    }

    private static void Take(List<string> statements, StringBuilder current)
    {
        string statement = current.ToString().Trim();
        current.Clear();

        if (statement.Length > 0)
        {
            statements.Add(statement);
        }
    }
}
