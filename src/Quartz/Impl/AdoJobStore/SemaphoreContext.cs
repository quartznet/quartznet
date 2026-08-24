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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The identity of the scheduler a lock handler locks for and the environment it locks in, handed to
/// <see cref="ISemaphore.Initialize" /> once by the job store before the semaphore is used.
/// </summary>
public sealed record SemaphoreContext
{
    /// <summary>
    /// Name of the scheduler whose scheduling data the lock protects.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The identifier of this scheduler node within a cluster.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Table prefix of the tables the ADO.NET job store uses. A handler that does not lock in the
    /// database ignores this.
    /// </summary>
    public required string TablePrefix { get; init; }

    /// <summary>
    /// The clock the store runs on, defaulting to <see cref="System.TimeProvider.System"/>. A handler
    /// that backs off between attempts waits on this rather than on wall time, so its retry behaviour
    /// can be tested without the test waiting too.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// How long the handler's statements may run before the provider cancels them, from
    /// <see cref="AdoJobStoreOptions.CommandTimeout" />. <see langword="null" /> leaves the provider's
    /// own default in place, and a handler that does not lock in the database ignores this.
    /// </summary>
    /// <remarks>
    /// The lock statement is where a timeout earns its keep: a node waiting on <c>QRTZ_LOCKS</c> behind
    /// a peer that stopped without releasing the row cannot make progress until the statement gives up.
    /// </remarks>
    public TimeSpan? CommandTimeout { get; init; }

    /// <summary>
    /// The factory the handler creates its logger from, defaulting to
    /// <see cref="NullLoggerFactory.Instance" />.
    /// </summary>
    /// <remarks>
    /// The job store passes the factory its container gave it, so lock contention, retries and the
    /// handler's own failures reach an application that never set
    /// <see cref="Quartz.Diagnostics.LogProvider" />. A handler initialized by hand is handed nothing
    /// and logs nowhere.
    /// </remarks>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;
}
