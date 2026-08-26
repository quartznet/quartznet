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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract against PostgreSQL.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class PostgresJobStoreContractTest : AdoJobStoreContractTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Npgsql;

    protected override IDriverDelegate CreateDriverDelegate() => new PostgreSQLDelegate();

    protected override ValueTask<string> PrepareDatabase()
    {
        // The container and its schema are the assembly's, started once by TestAssemblySetup.
        return new ValueTask<string>(ContainerConnectionString("PG_CONNECTION_STRING"));
    }
}
