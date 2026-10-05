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
/// How <c>UseWeaselForFirebird</c> applies a scheduler's schema.
/// </summary>
/// <remarks>
/// There is no lock to configure: Weasel.Firebird guards every statement, and appliers that race read
/// the schema again.
/// </remarks>
public sealed class FirebirdWeaselOptions
{
    /// <summary>Firebird 3's identifier limit, in bytes, which is the default.</summary>
    internal const int Firebird3MaxIdentifierLength = 31;

    /// <summary>Firebird 4's and 5's identifier limit, in characters.</summary>
    internal const int Firebird4MaxIdentifierLength = 63;

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
    /// The longest name Weasel may create: 31, Firebird 3's limit, by default; up to 63 for a database only
    /// Firebird 4 or later opens.
    /// </summary>
    /// <remarks>
    /// Under the default, a table prefix of at most six characters fits, because
    /// <c>IDX_&lt;prefix&gt;FT_INST_JOB_REQ_RCVRY</c> is 25 characters longer than the prefix. A longer
    /// prefix is refused before anything runs. Firebird refuses an over-long name rather than truncating
    /// it, so the limit is never used to shorten one. A value outside 31–63 is refused when the store is
    /// configured.
    /// </remarks>
    public int MaxIdentifierLength { get; set; } = Firebird3MaxIdentifierLength;
}
