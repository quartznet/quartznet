#region License

/*
 * Copyright 2009- Marko Lahma
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
/// A SQL Server specific driver delegate.
/// </summary>
/// <author>Marko Lahma</author>
public class SqlServerDelegate : StdAdoDelegate
{
    /// <summary>
    /// The size every string parameter is bound with, so one statement does not become a plan per
    /// value length: the widest <c>nvarchar</c> short of <c>max</c>.
    /// </summary>
    private const int MaxSizedStringLength = 4000;

    /// <inheritdoc />
    /// <remarks>
    /// The standard schema. The memory-optimized and pre-2016 variants under
    /// <c>database/tables/</c> have no counterpart: each is a deliberate departure a person chose for
    /// a particular deployment, not something a scheduler should decide to create.
    /// </remarks>
    protected override string? SchemaResourceName => "Quartz.Impl.AdoJobStore.Schema.create_sqlServer.sql";

    /// <summary>
    /// SQL Server names its row limit in the projection: <c>SELECT TOP n …</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.InProjection("TOP", count);

    /// <summary>
    /// T-SQL reads <c>[</c> as the start of a character class in a <c>LIKE</c> pattern, so a filter
    /// asking for a name containing <c>[a-z]</c> would match by class here and literally everywhere else.
    /// </summary>
    protected override string AdditionalLikeWildcards => "[";

    /// <summary>
    /// T-SQL joins strings with <c>+</c>; <c>||</c> is a syntax error unless the connection asks for
    /// ANSI string concatenation, which nothing here does.
    /// </summary>
    internal override string HistoryKeyExpression(string groupColumn, string nameColumn)
    {
        return "LOWER(" + groupColumn + " + '.' + " + nameColumn + ")";
    }

    /// <inheritdoc />
    public override void AddCommandParameter(
        DbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType = null,
        int? size = null)
    {
        // deeded for SQL Server CE
        if (paramValue is bool && dataType is null)
        {
            paramValue = (bool) paramValue ? 1 : 0;
        }

        // varbinary support
        if (size is null && dataType is not null && dataType.Equals(DbProvider.Metadata.BinaryParameterType))
        {
            size = -1;
        }

        // avoid size inferred from value that cause multiple query plans - except for a string longer
        // than that, which SqlClient would cut to the size rather than refuse: it is bound as
        // nvarchar(max). Only the execution history's captured log is ever that long.
        if (size is null && paramValue is string text)
        {
            size = text.Length > MaxSizedStringLength ? -1 : MaxSizedStringLength;
        }

        base.AddCommandParameter(cmd, paramName, paramValue, dataType, size);
    }
}
