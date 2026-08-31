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

namespace Quartz.Examples;

/// <summary>
/// Builds the in-memory scheduler most of the examples share.
/// </summary>
/// <remarks>
/// The examples have no host, so they use <see cref="QuartzSchedulerBuilder"/>, which creates a
/// container of its own and configures it with the same API an application would use under a host.
/// The examples that need to configure something of their own — a persistent store, a thread pool of
/// one — build their own instead, which is the same call chain with more of it written out.
/// </remarks>
internal static class ExampleScheduler
{
    public static ValueTask<IScheduler> Create(
        string instanceName = "ExampleDefaultQuartzScheduler",
        int maxConcurrency = 10,
        TimeSpan? misfireThreshold = null,
        CancellationToken cancellationToken = default)
    {
        return QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = instanceName)
                .UseDefaultThreadPool(maxConcurrency)
                .UseInMemoryStore(options => options.MisfireThreshold = misfireThreshold ?? TimeSpan.FromSeconds(60)))
            .BuildScheduler(cancellationToken);
    }
}
