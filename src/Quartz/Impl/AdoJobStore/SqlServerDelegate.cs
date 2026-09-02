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

        // avoid size inferred from value that cause multiple query plans
        if (size is null && paramValue is string)
        {
            size = 4000;
        }

        base.AddCommandParameter(cmd, paramName, paramValue, dataType, size);
    }
}
