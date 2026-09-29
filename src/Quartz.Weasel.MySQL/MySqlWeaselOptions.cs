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
/// How <c>UseWeaselForMySql</c> applies a scheduler's schema.
/// </summary>
public sealed class MySqlWeaselOptions
{
    /// <summary>
    /// The user lock every apply takes by default: <c>quartz:migrate</c>.
    /// </summary>
    /// <remarks>
    /// A MySQL user lock is scoped to the server, not to a database, so schedulers on different
    /// databases of one server take turns on it too. Give each its own <see cref="LockName" /> to keep
    /// them apart.
    /// </remarks>
    public const string DefaultLockName = "quartz:migrate";

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
    /// The <c>GET_LOCK</c> name every apply of this schema takes, on startup and from the command line.
    /// At most 64 characters.
    /// </summary>
    public string LockName { get; set; } = DefaultLockName;

    /// <summary>
    /// How long an apply waits for another session holding <see cref="LockName" /> before it gives up.
    /// </summary>
    /// <remarks>
    /// A process that gives up fails its startup, unless the active JasperFx profile's
    /// <c>ResourceMigrationFailureMode</c> is <c>ContinueOnFailures</c>, in which case it starts against the
    /// schema the lock holder is applying.
    /// </remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
