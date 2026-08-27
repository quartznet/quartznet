/*
* Copyright 2004-2009 James House
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

using System.Data.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is a driver delegate for the SQLiteDelegate ADO.NET driver.
/// </summary>
/// <author>Marko Lahma</author>
public class SQLiteDelegate : StdAdoDelegate
{
    /// <summary>
    /// SQLite pages with LIMIT/OFFSET rather than the ANSI clause. A negative LIMIT means no limit,
    /// which is how SQLite writes an offset without one.
    /// </summary>
    protected override string ApplyPaging(string sql, bool takeLimited)
    {
        return takeLimited
            ? sql + " LIMIT @pageTake OFFSET @pageSkip"
            : sql + " LIMIT -1 OFFSET @pageSkip";
    }

    /// <summary>
    /// Binds the LIMIT/OFFSET parameters in the order the clause names them, which is the reverse of
    /// the ANSI clause's order and matters to providers that bind positionally.
    /// </summary>
    protected override void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        if (takeLimited)
        {
            AddCommandParameter(cmd, "pageTake", take);
        }

        AddCommandParameter(cmd, "pageSkip", skip);
    }

    /// <summary>
    /// SQLite limits rows with a trailing <c>LIMIT n</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.AtStatementEnd("LIMIT", count);
}
