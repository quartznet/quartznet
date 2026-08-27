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

using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// Builds a scheduler whose job store reports itself clustered, which is the only condition under
/// which an instance id is generated at all: <c>DefaultSchedulerFactory.GenerateInstanceId</c> hands
/// back <see cref="QuartzSchedulerOptions.DefaultInstanceId" /> without asking the generator anything
/// when the store shares its database with nobody.
/// </summary>
/// <remarks>
/// The store is in-memory and merely says it is clustered. That is all an instance-id test needs — the
/// id is settled before the store is asked to do anything — and it is why these tests need no database,
/// which is what the <c>TODO</c> in <see cref="SystemPropertyInstanceIdGeneratorTest" /> was waiting
/// for.
/// </remarks>
internal static class ClusteredNodeBuilder
{
    /// <summary>
    /// Builds a node's factory. The caller owns it: disposing the factory disposes the container it
    /// built and shuts the scheduler down.
    /// </summary>
    public static StandaloneSchedulerFactory Build(NameValueCollection properties = null)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();

        if (properties is not null)
        {
            builder = builder.UseProperties(properties);
        }

        return builder
            .UseJobStore(provider => new ClusteredInMemoryJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider)))
            .Build();
    }

    /// <summary>
    /// An in-memory store that answers <see cref="IJobStore.Clustered" /> the way a database-backed
    /// store configured for a cluster does, and behaves in every other respect like the store it wraps.
    /// </summary>
    private sealed class ClusteredInMemoryJobStore : DelegatingJobStore
    {
        public ClusteredInMemoryJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public override bool Clustered => true;
    }
}
