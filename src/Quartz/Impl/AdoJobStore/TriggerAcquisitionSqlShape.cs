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
public readonly record struct TriggerAcquisitionSqlShape(int MaxCount, int ExcludedJobTypeBucket);
