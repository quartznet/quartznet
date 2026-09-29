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
/// How <c>UseWeaselForOracle</c> applies a scheduler's schema.
/// </summary>
/// <remarks>
/// There is no lock to configure: appliers race and read the schema again, because the one lock Oracle
/// has for this, <c>DBMS_LOCK</c>, needs a grant the store itself never needs.
/// </remarks>
public sealed class OracleWeaselOptions
{
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
}
