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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Aspire;

/// <summary>
/// The repro from #4104, end to end: a host, its scheduler factory, the scheduler, and the store the
/// scheduler says it runs on.
/// </summary>
/// <remarks>
/// On a SQLite file rather than the issue's unreachable Postgres, so the persistent store comes up for
/// real and both orders can be asked the same question. The issue told the two stores apart by whether
/// the scheduler failed to connect; here both succeed, and the metadata answers.
/// </remarks>
public sealed class AspireStoreRegistrationOrderTest
{
    private const string ConnectionName = "schedulerdb";

    private const string SchedulerName = "Scheduler";

    [Test]
    public async Task TheSchedulerRunsOnTheDatabaseStoreInEitherOrder(
        [Values] bool storeFirst,
        [Values] bool named)
    {
        using SqliteTestDatabase database = new("aspire-store-order");
        HostApplicationBuilder builder = AspireApplication.WorkerWith(database.ConnectionString, ConnectionName);

        if (storeFirst)
        {
            AddStore(builder);
        }

        if (named)
        {
            builder.Services.AddQuartz(SchedulerName, q => q.UseDefaultThreadPool(2));
        }
        else
        {
            builder.Services.AddQuartz(q => q.UseDefaultThreadPool(2));
        }

        if (!storeFirst)
        {
            AddStore(builder);
        }

        using IHost host = builder.Build();

        ISchedulerFactory factory = named
            ? host.Services.GetRequiredKeyedService<ISchedulerFactory>(SchedulerName)
            : host.Services.GetRequiredService<ISchedulerFactory>();

        IScheduler scheduler = await factory.GetScheduler();
        try
        {
            SchedulerMetadata metadata = await scheduler.GetMetadata();

            metadata.JobStorePersistent.Should().BeTrue(
                "the remarks promise the container comes out the same before or after AddQuartz, and a "
                + "schedule on the in-memory store silently does not survive a restart");
            metadata.JobStoreTypeName.Should().Contain(nameof(LocalTransactionJobStore));
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    private static void AddStore(HostApplicationBuilder builder)
    {
        builder.AddQuartzPersistentStore(ConnectionName, settings =>
        {
            settings.SchemaProvisioning = SchemaProvisioning.CreateIfMissing;
            settings.DisableHealthChecks = true;
        });
    }
}
