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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is a driver delegate for the PostgreSQL ADO.NET driver.
/// </summary>
/// <author>Marko Lahma</author>
public class PostgreSQLDelegate : StdAdoDelegate
{
    /// <inheritdoc />
    protected override string? SchemaResourceName => "Quartz.Impl.AdoJobStore.Schema.create_postgres.sql";

    /// <summary>
    /// PostgreSQL limits rows with a trailing <c>LIMIT n</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.AtStatementEnd("LIMIT", count);

    /// <summary>
    /// PostgreSQL has <c>percentile_cont</c> as an ordered-set aggregate, so the run statistics' percentiles
    /// are the database's.
    /// </summary>
    internal override bool HistoryHasPercentileAggregate => true;
}
