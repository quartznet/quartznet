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

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The migrations a database has to run, read from <c>database/migrations/</c> rather than listed, so a
/// new script is in every chain that should run it the moment it lands.
/// </summary>
/// <remarks>
/// Each entry is a folder and a script name without its <c>_&lt;dialect&gt;.sql</c>, the shape
/// <see cref="MigrationScriptTest.MigrationScript" /> takes.
/// </remarks>
internal static class MigrationChains
{
    private static readonly string[] Dialects = ["sqlServer", "postgres", "mysql_innodb", "oracle", "sqlite", "firebird"];

    /// <summary>
    /// The two halves of the 4.0 upgrade, in the order an operator runs them: the mandatory one while
    /// 3.x nodes are still up, and the index set once the last of them has gone.
    /// </summary>
    /// <remarks>
    /// Named rather than read, because the folder's ordinal order runs <c>indexes</c> first.
    /// </remarks>
    public static readonly (string Version, string Name)[] Upgrade40 =
    [
        ("4.0", "schema_30_to_40_upgrade"),
        ("4.0", "schema_30_to_40_indexes")
    ];

    /// <summary>
    /// Every migration a database at <paramref name="version" /> has not had: each folder after it,
    /// oldest first, and each script in a folder in ordinal order. Optional ones included, since the
    /// schema they are compared with is a fresh install's, which has every table.
    /// </summary>
    /// <param name="version">A folder from 4.0 on; before it, the 4.0 pair would run in the wrong order.</param>
    public static (string Version, string Name)[] Since(string version)
    {
        Version floor = Version.Parse(version);
        if (floor < new Version(4, 0))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version,
                "The 4.0 folder's two scripts run upgrade before indexes, which ordinal order reverses; chain Upgrade40 by name.");
        }

        string root = MigrationsDirectory();

        return
        [
            .. Directory.GetDirectories(root)
                .Select(folder => (Folder: Path.GetFileName(folder), Version: Version.Parse(Path.GetFileName(folder))))
                .Where(x => x.Version > floor)
                .OrderBy(x => x.Version)
                .SelectMany(x => Directory.GetFiles(Path.Combine(root, x.Folder), "*.sql")
                    .Select(file => ScriptName(Path.GetFileName(file)))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(name => (x.Folder, name)))
        ];
    }

    /// <summary>A file's script name: its name less the dialect it is written for.</summary>
    private static string ScriptName(string file)
    {
        foreach (string dialect in Dialects)
        {
            string suffix = $"_{dialect}.sql";
            if (file.EndsWith(suffix, StringComparison.Ordinal))
            {
                return file[..^suffix.Length];
            }
        }

        throw new InvalidOperationException($"'{file}' names none of the six dialects, so no chain can run it.");
    }

    private static string MigrationsDirectory()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "database", "migrations")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new DirectoryNotFoundException($"No database/migrations above {AppContext.BaseDirectory}.")
            : Path.Combine(current.FullName, "database", "migrations");
    }
}
