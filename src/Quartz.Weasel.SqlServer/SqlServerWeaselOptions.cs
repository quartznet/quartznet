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

using JasperFx;

namespace Quartz;

/// <summary>
/// How <c>UseWeaselForSqlServer</c> applies a scheduler's schema.
/// </summary>
public sealed class SqlServerWeaselOptions
{
    /// <summary>
    /// The application lock every apply takes by default: <c>quartz:migrate</c>.
    /// </summary>
    /// <remarks>
    /// Wolverine's migration lock on SQL Server is <c>4006</c> and Polecat's is
    /// <c>polecat:migrate:&lt;schema&gt;</c>. <c>sp_getapplock</c> scopes a resource to the current
    /// database, so schedulers on different databases never wait for one another.
    /// </remarks>
    public const string DefaultLockResource = "quartz:migrate";

    /// <summary>
    /// How far the schema is applied at startup, or <see langword="null" /> to follow the active JasperFx
    /// profile's <c>ResourceAutoCreate</c> when JasperFx is registered, and
    /// <see cref="JasperFx.AutoCreate.CreateOrUpdate" /> when it is not.
    /// </summary>
    /// <remarks>
    /// <see cref="JasperFx.AutoCreate.None" /> applies nothing at startup; <c>db-apply</c> and
    /// <c>resources setup</c> still do, and the store still validates the schema before it starts.
    /// </remarks>
    public AutoCreate? AutoCreate { get; set; }

    /// <summary>
    /// The <c>sp_getapplock</c> resource every apply of this schema takes, on startup and from the
    /// command line. At most 255 characters.
    /// </summary>
    /// <remarks>
    /// Schedulers sharing a database may share the resource: their applies then take turns, which is all
    /// the lock is for.
    /// </remarks>
    public string LockResource { get; set; } = DefaultLockResource;

    /// <summary>
    /// How long an apply waits for another session holding <see cref="LockResource" /> before it gives up.
    /// </summary>
    /// <remarks>
    /// A process that gives up fails its startup, unless the active JasperFx profile's
    /// <c>ResourceMigrationFailureMode</c> is <c>ContinueOnFailures</c>, in which case it starts against the
    /// schema the lock holder is applying.
    /// </remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
