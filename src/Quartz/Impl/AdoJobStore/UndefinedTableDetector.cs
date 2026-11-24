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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Decides whether a failure says that a table does not exist, as opposed to anything else a statement can fail
/// with: a connection that dropped, a timeout, a refused permission, a lock.
/// </summary>
/// <remarks>
/// <para>
/// Asked where the store turns a missing optional table into a message naming the script that creates it. Only
/// this answer may: a database that went away after the connection opened would otherwise read as one nobody
/// migrated, and the page would hide a column and name a script instead of saying the read failed.
/// </para>
/// <para>
/// Each dialect's own signal, read without a reference to any driver:
/// <list type="bullet">
/// <item><description>PostgreSQL: SQLSTATE <c>42P01</c>, undefined table.</description></item>
/// <item><description>MySQL: SQLSTATE <c>42S02</c>, base table not found.</description></item>
/// <item><description>Firebird: error <c>335544580</c>, Table unknown, among the codes on its <c>Errors</c>. Its SQLSTATE
/// is the first code's, the generic Dynamic SQL Error's, and says nothing of a table.</description></item>
/// <item><description>SQL Server: error <c>208</c>, invalid object name. Both SqlClients leave the SQLSTATE null.</description></item>
/// <item><description>Oracle: <c>ORA-00942</c>, table or view does not exist, which the driver's message starts with.</description></item>
/// <item><description>SQLite: "no such table", the one message SQLite gives for it under its generic error code.</description></item>
/// </list>
/// The chain of inner exceptions is walked, as the store wraps what it catches.
/// </para>
/// </remarks>
internal static class UndefinedTableDetector
{
    /// <summary>Whether <paramref name="exception" />, or anything it wraps, says that a table does not exist.</summary>
    public static bool IsUndefinedTable(Exception exception)
    {
        for (Exception? candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (TransientErrorDetector.GetSqlState(candidate) is "42P01" or "42S02"
                || TransientErrorDetector.HasErrorNumber(candidate, static number => number is 208 or 335544580)
                || candidate.Message.Contains("ORA-00942", StringComparison.Ordinal)
                || (candidate is DbException && candidate.Message.Contains("no such table", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
