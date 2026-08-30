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
/// What a persistent job store does about its schema when it starts.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the <c>PerformSchemaValidation</c> flag, which could only say whether the store
/// checked. The check and the creation are one decision — a store that may create its schema
/// obviously validates it too — so they are one setting with three positions rather than two flags
/// that can contradict each other.
/// </para>
/// <para>
/// There is deliberately no "create or migrate" position. Nothing in the schema records which
/// version it is, and SQLite's <c>ADD COLUMN</c> has no conditional form, so a store cannot tell a
/// schema that is one release old from one that is five and cannot safely try. Upgrading a schema
/// stays a decision someone makes with the scripts under <c>database/migrations/</c>.
/// </para>
/// </remarks>
public enum SchemaProvisioning
{
    /// <summary>
    /// Assume the schema is there. The store issues its first statement against whatever it finds.
    /// </summary>
    /// <remarks>
    /// The failure this saves a few milliseconds at the cost of is a bad one: the first operation to
    /// touch a missing table reports a provider error naming one table, at whatever moment the
    /// scheduler happened to need it, rather than a startup failure naming the schema.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Check at startup that every table the store reads and writes can be queried, and refuse to
    /// start when one cannot. The default.
    /// </summary>
    /// <remarks>
    /// The check is table-level rather than column-level, so a schema that has every table but is
    /// missing a column added by a later release starts and then fails on the first statement naming
    /// that column. <c>database/migrations/4.0/</c> is what closes that gap.
    /// </remarks>
    Validate = 1,

    /// <summary>
    /// Create whatever the schema is missing, then validate it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only ever creates: no object is altered and none is dropped, so this is safe to leave on
    /// against a schema that already exists and cannot turn a mis-typed table prefix into data loss.
    /// It is equally not an upgrade — a schema that has every table but is missing a column added by
    /// a later release is left exactly as it is, and then fails validation's successor, the first
    /// statement that names the column.
    /// </para>
    /// <para>
    /// Not the default, because creating tables needs DDL permission and a production database is
    /// usually right not to grant the scheduler any. It is what a test fixture, a container that
    /// starts with an empty volume, or a desktop application with a SQLite file wants.
    /// </para>
    /// <para>
    /// Safe on several nodes starting at once: whichever loses the race sees its create fail, finds
    /// the schema another node created, and carries on.
    /// </para>
    /// </remarks>
    CreateIfMissing = 2,
}
