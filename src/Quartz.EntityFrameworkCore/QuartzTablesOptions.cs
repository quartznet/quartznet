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
    /// The names are folded as the database folds the store's unquoted ones: to lower case on PostgreSQL, to upper
    /// case on Oracle and Firebird, so <c>QRTZ_</c> and <c>qrtz_</c> name the same tables there. A prefix that makes
    /// a name longer than the database takes is refused; on Firebird that is 31 bytes, so a prefix of at most six
    /// bytes of UTF-8, six characters of ASCII, until <see cref="MaxIdentifierLength" /> says more.
    /// </remarks>
    public string TablePrefix { get; set; } = "QRTZ_";

    /// <summary>
    /// The schema the tables are in, or <see langword="null" /> for the model's default schema, which is the
    /// connection's when the model sets none.
    /// </summary>
    /// <remarks>
    /// PostgreSQL and SQL Server only: on MySQL, SQLite and Firebird the tables are in the connection's database,
    /// and on Oracle in the session's schema, so a schema here is refused.
    /// </remarks>
    public string? Schema { get; set; }

    /// <summary>
    /// The longest name the model may give, in bytes, or <see langword="null" /> for the database's own limit,
    /// which on Firebird is 31, Firebird 3's; up to 63 for a database only Firebird 4 or later opens.
    /// </summary>
    /// <remarks>
    /// Firebird only: every other database has one limit, so a value here is refused there, and so is one outside
    /// 31–63. Under 31 a table prefix of at most six bytes fits, because
    /// <c>IDX_&lt;prefix&gt;FT_INST_JOB_REQ_RCVRY</c> is 25 bytes longer than the prefix; under 63, one of at most
    /// 38. Firebird refuses an over-long name rather than truncating it, so the limit is never used to shorten one.
    /// </remarks>
    public int? MaxIdentifierLength { get; set; }
}
