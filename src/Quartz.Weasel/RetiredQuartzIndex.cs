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

using Weasel.Core;

namespace Quartz.Weasel;

/// <summary>
/// An index name Quartz no longer creates, dropped when it is still on the table it was created on.
/// </summary>
/// <remarks>
/// <para>
/// The tables are add-only, so Weasel keeps anything the model does not declare — right for an
/// application's own index and wrong for the ones 3.x created and 4.x retired. Each dialect's model names
/// them, the same set <c>database/migrations/4.0/schema_30_to_40_indexes_&lt;dialect&gt;.sql</c> drops, and
/// only when the index is on the Quartz table it belonged to.
/// </para>
/// <para>
/// The dialect says how its catalog is asked, a count of matching indexes, and how the index is dropped;
/// the rest is here.
/// </para>
/// </remarks>
internal abstract class RetiredQuartzIndex : SchemaObjectBase
{
    protected RetiredQuartzIndex(DbObjectName identifier, string table) : base(identifier)
    {
        Table = table;
    }

    /// <summary>The table the index was created on, without its schema.</summary>
    protected string Table { get; }

    /// <summary>A drop when the catalog counted the index, and nothing to do otherwise.</summary>
    public sealed override async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        bool present = await reader.ReadAsync(ct).ConfigureAwait(false)
                       && await ReadExistsCountAsync(reader, ct).ConfigureAwait(false) > 0;

        return new SchemaObjectDelta(this, present ? SchemaPatchDifference.Update : SchemaPatchDifference.None);
    }

    /// <summary>Nothing: a retired index is never created.</summary>
    public sealed override void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
    }
}
