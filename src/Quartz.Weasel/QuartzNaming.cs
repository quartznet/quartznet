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

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// What every dialect's <c>QuartzTableNaming</c> does the same way, over whichever Weasel types the dialect has.
/// </summary>
/// <remarks>
/// What differs stays in the dialect: how its catalog spells a name, what its constraints are called, and
/// how its index states a column's direction.
/// </remarks>
internal static class QuartzNaming
{
    /// <summary><c>IDX_{1}T_NFT_ST</c> in every script: the table prefix without its schema, then the index's own suffix.</summary>
    public static string IndexName(string prefix, string suffix) => $"IDX_{prefix}{suffix}";

    /// <summary>
    /// A foreign key from <paramref name="columns" /> to <paramref name="referencedColumns" /> of
    /// <paramref name="referencedTable" />, deleting its rows with the parent's only when
    /// <paramref name="cascade" />.
    /// </summary>
    public static TForeignKey Links<TForeignKey>(
        this TForeignKey foreignKey,
        DbObjectName referencedTable,
        string[] columns,
        string[] referencedColumns,
        bool cascade)
        where TForeignKey : ForeignKeyBase
    {
        foreignKey.LinkedTable = referencedTable;
        foreignKey.ColumnNames = columns;
        foreignKey.LinkedNames = referencedColumns;
        foreignKey.DeleteAction = cascade ? CascadeAction.Cascade : CascadeAction.NoAction;
        return foreignKey;
    }

    /// <summary>
    /// Adds an index over <paramref name="columns" /> to a table, naming the columns in
    /// <paramref name="descending" /> in its <paramref name="descendingColumns" />: the dialects whose
    /// catalog reads a column's direction back one column at a time.
    /// </summary>
    public static void AddIndex<TIndex>(
        this IList<TIndex> indexes,
        TIndex index,
        string[] columns,
        ISet<string> descendingColumns,
        string[]? descending)
        where TIndex : ITableIndex
    {
        index.Columns = columns;

        foreach (string column in descending ?? [])
        {
            descendingColumns.Add(column);
        }

        indexes.Add(index);
    }
}
