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
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Everything an <see cref="AdoJobStoreBase" /> is built from, as one argument.
/// </summary>
/// <remarks>
/// <para>
/// A store that derives from <see cref="AdoJobStoreBase" /> takes one of these and chains it, rather
/// than restating each dependency and forwarding it — which is what
/// <see cref="LocalTransactionJobStore" />, <see cref="ExternalTransactionJobStore" /> and every store
/// the custom-store how-to describes used to do, twelve parameters at a time. A dependency added here
/// reaches all of them without any of them changing.
/// </para>
/// <para>
/// The container registers this per scheduler and builds it with
/// <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities" />, so each property is
/// resolved exactly as the constructor parameter it replaced was: from that scheduler's own
/// registrations, and left at its default where a store was never given one.
/// </para>
/// </remarks>
/// <param name="SchedulerSignaler">How the store tells the scheduler that something has changed.</param>
/// <param name="TypeLoader">Loads the job types the store reads back out of the database.</param>
/// <param name="TimeProvider">The clock every time this store reads or writes comes from.</param>
/// <param name="SchedulerOptions">The scheduler's identity, as configured.</param>
/// <param name="StoreOptions">How this store talks to its database.</param>
/// <param name="ClusteringOptions">Whether this node is part of a cluster, and how it checks in.</param>
/// <param name="ObjectSerializer">Turns job data and calendars into what the database stores.</param>
/// <param name="DbProvider">Opens the connections this store runs its statements on.</param>
/// <param name="DriverDelegate">The dialect the store speaks to its database in.</param>
/// <param name="LockHandler">
/// The lock the store serializes its operations with, or <see langword="null" /> to let
/// <see cref="AdoJobStoreBase.Initialize" /> choose one once it knows which database this is.
/// </param>
/// <param name="TriggerPersistenceDelegates">
/// The persistence delegates for trigger types beyond the ones Quartz ships.
/// </param>
/// <param name="LoggerFactory">
/// What the store and everything it owns create their loggers from, or <see langword="null" /> to read
/// the ambient one.
/// </param>
internal sealed record AdoJobStoreDependencies(
    ISchedulerSignaler SchedulerSignaler,
    ITypeLoader TypeLoader,
    TimeProvider TimeProvider,
    IOptions<QuartzSchedulerOptions> SchedulerOptions,
    IOptions<AdoJobStoreOptions> StoreOptions,
    IOptions<ClusteringOptions> ClusteringOptions,
    IObjectSerializer ObjectSerializer,
    IDbProvider DbProvider,
    IDriverDelegate DriverDelegate,
    ILockHandler? LockHandler = null,
    IEnumerable<ITriggerPersistenceDelegate>? TriggerPersistenceDelegates = null,
    ILoggerFactory? LoggerFactory = null);
