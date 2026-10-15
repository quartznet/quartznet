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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Everything about one acquisition attempt that changes the text of the statement, and nothing that
/// does not.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="StdAdoDelegate.GetSelectNextTriggerToAcquireSql" /> is handed, and it is
/// also the key the finished statement is cached under — the same object, because a statement is a
/// pure function of its shape. Two attempts of the same shape are the same string, so the dialect
/// builds it once.
/// </para>
/// <para>
/// It exists so that the next acquisition dimension adds a property here rather than a parameter to
/// every dialect delegate that overrides the hook. A dialect that only splices in a row limit reads
/// <see cref="MaxCount" /> and hands the whole shape to <c>base</c>, and so keeps working when a
/// dimension it has never heard of is added.
/// </para>
/// </remarks>
/// <param name="MaxCount">
/// The most rows the statement may return, already clamped to at least one by the caller. A dialect
/// splices this into its <c>TOP</c>, <c>LIMIT</c>, <c>ROWS</c> or <c>rownum</c> clause.
/// </param>
/// <param name="ExcludedJobTypeBucket">
/// How many <c>NOT IN</c> terms the job-type exclusion clause carries, or zero for no clause at all.
/// It is a bucket rather than the caller's exact count because the parameter list is padded up to
/// one of a few sizes, which is what keeps the statement cache — and the database's plan cache —
/// from growing an entry per distinct exclusion count.
/// </param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TriggerAcquisitionSqlShape(int MaxCount, int ExcludedJobTypeBucket)
{
    /// <summary>
    /// The execution-group routing clause the statement carries, or <see langword="default" /> for
    /// none.
    /// </summary>
    /// <remarks>
    /// An <c>init</c> property rather than a third positional parameter: the constructor is a public
    /// signature a dialect's tests construct, and a dimension added later goes beside it. The count is
    /// exact rather than bucketed, unlike <see cref="ExcludedJobTypeBucket" />, because the filter is
    /// fixed for a node's lifetime: one statement per node, however it is spelled.
    /// </remarks>
    public ExecutionGroupClauseShape ExecutionGroups { get; init; }
}

/// <summary>
/// What the execution-group routing clause of a statement looks like, and nothing about which groups
/// it names: the names are parameters.
/// </summary>
/// <remarks>
/// <see langword="default" /> is no clause. The six shapes it can take are in
/// <c>StdAdoConstants.ExecutionGroupPredicate</c>; the <c>IS NULL</c> term a refuse-list carries is
/// load-bearing, since <c>NULL NOT IN (…)</c> is unknown and would drop every ungrouped row.
/// </remarks>
/// <param name="IsServeList">
/// Whether the named groups are what the node serves (<c>IN</c>) or what it refuses (<c>NOT IN</c>).
/// </param>
/// <param name="IncludesUngrouped">Whether the list names the triggers that carry no execution group.</param>
/// <param name="NamedCount">How many named groups the clause binds, as <c>@servedGroup0000…</c> or <c>@refusedGroup0000…</c>.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ExecutionGroupClauseShape(bool IsServeList, bool IncludesUngrouped, int NamedCount)
{
    /// <summary>
    /// Whether this is a clause at all.
    /// </summary>
    public bool HasClause => IncludesUngrouped || NamedCount > 0;

    /// <summary>
    /// The shape that selects exactly the rows this one leaves out — what the sweep asks to find the
    /// misfired triggers no node is serving.
    /// </summary>
    public ExecutionGroupClauseShape Complement => this with { IsServeList = !IsServeList };

    /// <summary>
    /// The shape of the clause <paramref name="served" /> or <paramref name="refused" /> spells, or
    /// <see langword="default" /> when both are <see langword="null" />.
    /// </summary>
    public static ExecutionGroupClauseShape Of(
        IReadOnlyCollection<ExecutionGroupScope>? served,
        IReadOnlyCollection<ExecutionGroupScope>? refused)
    {
        IReadOnlyCollection<ExecutionGroupScope>? scopes = served ?? refused;
        if (scopes is null)
        {
            return default;
        }

        bool includesUngrouped = false;
        int named = 0;
        foreach (ExecutionGroupScope scope in scopes)
        {
            if (scope.IsDefault)
            {
                includesUngrouped = true;
            }
            else
            {
                named++;
            }
        }

        return new ExecutionGroupClauseShape(served is not null, includesUngrouped, named);
    }
}
