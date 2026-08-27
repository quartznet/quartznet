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

using System.Runtime.InteropServices;

using static System.FormattableString;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// How a dialect says "at most this many rows", as a slot the statement has for it rather than as an
/// edit to finished SQL.
/// </summary>
/// <remarks>
/// <para>
/// There is no ANSI row limit, so every database spells it somewhere else in the statement, and a
/// dialect used to reach into a statement it does not own to put it there — SQL Server cut the first
/// six characters off and pasted its own <c>SELECT</c> back on, which was correct only while the
/// statement began with exactly that keyword and nothing said it had to. A dialect now says which of
/// the three places its clause belongs in, and the statement is built with it already there.
/// </para>
/// <para>
/// The three cover every shipped dialect and, as far as row limiting goes, every SQL database:
/// <see cref="InProjection" /> for SQL Server's <c>TOP</c>, <see cref="AtStatementEnd" /> for the
/// <c>LIMIT</c> of PostgreSQL, MySQL and SQLite and the <c>ROWS</c> of Firebird, and
/// <see cref="InEnclosingSelect" /> for Oracle's <c>rownum</c>.
/// </para>
/// <para>
/// The count is spliced into the text rather than bound as a parameter, because a row limit is part
/// of the statement's shape on several of these databases — SQL Server will not take <c>TOP</c> from
/// a parameter without parentheses, and a limit that varies per call would churn the plan cache
/// anyway. The value is Quartz's own batch size, never anything a caller supplies.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SqlRowLimit
{
    private readonly Placement placement;
    private readonly string? clause;
    private readonly int count;

    private SqlRowLimit(Placement placement, string clause, int count)
    {
        this.placement = placement;
        this.clause = clause;
        this.count = count;
    }

    /// <summary>
    /// No limit at all: the statement is built exactly as it would be for a database that cannot
    /// limit rows, which is what <see cref="StdAdoDelegate" /> itself assumes.
    /// </summary>
    public static SqlRowLimit Unlimited => default;

    /// <summary>
    /// A limit that sits in the projection, immediately after the <c>SELECT</c> keyword —
    /// <c>SELECT TOP 5 …</c>.
    /// </summary>
    /// <param name="keyword">The keyword introducing the limit, <c>TOP</c> on SQL Server.</param>
    /// <param name="count">The most rows the statement may return.</param>
    public static SqlRowLimit InProjection(string keyword, int count) => new(Placement.Projection, keyword, count);

    /// <summary>
    /// A limit that follows the whole statement, after its <c>ORDER BY</c> — <c>… LIMIT 5</c>.
    /// </summary>
    /// <param name="keyword">
    /// The keyword introducing the limit: <c>LIMIT</c> on PostgreSQL, MySQL and SQLite, <c>ROWS</c>
    /// on Firebird.
    /// </param>
    /// <param name="count">The most rows the statement may return.</param>
    public static SqlRowLimit AtStatementEnd(string keyword, int count) => new(Placement.StatementEnd, keyword, count);

    /// <summary>
    /// A limit expressed by an enclosing <c>SELECT</c> that filters on a row-number pseudo-column —
    /// <c>SELECT * FROM (…) WHERE rownum &lt;= 5</c>.
    /// </summary>
    /// <param name="pseudoColumn">The pseudo-column carrying the row number, <c>rownum</c> on Oracle.</param>
    /// <param name="count">The most rows the statement may return.</param>
    public static SqlRowLimit InEnclosingSelect(string pseudoColumn, int count) => new(Placement.EnclosingSelect, pseudoColumn, count);

    /// <summary>
    /// What goes immediately after the <c>SELECT</c> keyword, or nothing.
    /// </summary>
    /// <remarks>
    /// Carries its own spaces on both sides, so that a statement with no limit is the same text it
    /// was before there was a slot to fill.
    /// </remarks>
    internal string AfterSelect => placement == Placement.Projection ? Invariant($" {clause} {count} ") : "";

    /// <summary>
    /// What goes after the last of the statement's own clauses, or nothing.
    /// </summary>
    internal string AtEnd => placement == Placement.StatementEnd ? Invariant($" {clause} {count}") : "";

    /// <summary>
    /// Wraps a finished statement in the enclosing <c>SELECT</c> this limit asked for, or hands it
    /// back untouched. Unlike the other two this needs no slot: it assumes nothing about the
    /// statement beyond its being one.
    /// </summary>
    internal string Enclose(string sql) =>
        placement == Placement.EnclosingSelect
            ? Invariant($"SELECT * FROM ({sql}) WHERE {clause} <= {count}")
            : sql;

    private enum Placement
    {
        /// <summary>No limit. The default, so that <c>default(SqlRowLimit)</c> is <see cref="Unlimited" />.</summary>
        None = 0,
        Projection,
        StatementEnd,
        EnclosingSelect
    }
}
