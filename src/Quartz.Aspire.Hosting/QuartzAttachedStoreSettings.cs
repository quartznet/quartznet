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

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// How the dashboard reads a database <c>WithAttachedStore</c> attached: what the nodes that write it were
/// configured with.
/// </summary>
/// <remarks>
/// Every setting is the dashboard host's <c>Quartz:Dashboard:Targets:&lt;target&gt;:*</c> key of the same
/// name. Unset, the host's default applies.
/// </remarks>
public sealed class QuartzAttachedStoreSettings
{
    /// <summary>
    /// The database's ADO.NET provider, one of Quartz's <c>DataSourceOptions.Providers</c> names:
    /// <c>Npgsql</c>, <c>SqlServer</c>, <c>MySqlConnector</c>, <c>OracleODPManaged</c>,
    /// <c>SQLite-Microsoft</c> or <c>Firebird</c>. Unset, the host works it out from the connection string, as
    /// <c>Quartz.Aspire</c> does.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// The prefix on Quartz's table names. Unset, <c>QRTZ_</c>.
    /// </summary>
    public string? TablePrefix { get; set; }

    /// <summary>
    /// The serializer the nodes write job data and calendars with: <c>SystemTextJson</c>, the default, or
    /// <c>Newtonsoft</c>.
    /// </summary>
    public string? Serializer { get; set; }

    /// <summary>
    /// Whether the nodes keep their execution history in the database, which is what puts executions on the
    /// dashboard's History page for this database's schedulers.
    /// </summary>
    public bool ExecutionHistory { get; set; }

    /// <summary>
    /// How often the database is asked again which schedulers are in it. Unset, one minute;
    /// <see cref="TimeSpan.Zero" /> asks once, at start-up.
    /// </summary>
    public TimeSpan? RediscoveryInterval { get; set; }
}
