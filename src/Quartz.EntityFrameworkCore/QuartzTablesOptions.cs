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

namespace Quartz;

/// <summary>
/// What Quartz's tables are called in an Entity Framework Core model: the table prefix and the schema, which
/// together name what the job store's <c>TablePrefix</c> names.
/// </summary>
/// <remarks>
/// A job store whose <c>TablePrefix</c> is <c>QRTZ_</c> needs nothing set here. One whose <c>TablePrefix</c> is
/// <c>quartz.QRTZ_</c> has <see cref="Schema" /> <c>quartz</c> and <see cref="TablePrefix" /> <c>QRTZ_</c>.
/// </remarks>
public sealed class QuartzTablesOptions
{
    /// <summary>
    /// What every table and index name starts with, without a schema: <c>QRTZ_</c> by default, which is the
    /// job store's.
    /// </summary>
    /// <remarks>
    /// On PostgreSQL the names are folded to lower case, as PostgreSQL folds the store's unquoted ones, so
    /// <c>QRTZ_</c> and <c>qrtz_</c> name the same tables there.
    /// </remarks>
    public string TablePrefix { get; set; } = "QRTZ_";

    /// <summary>
    /// The schema the tables are in, or <see langword="null" /> for the model's default schema, which is the
    /// connection's when the model sets none.
    /// </summary>
    /// <remarks>
    /// PostgreSQL and SQL Server only: on MySQL and SQLite the tables are in the connection's database, and a
    /// schema here is refused.
    /// </remarks>
    public string? Schema { get; set; }
}
