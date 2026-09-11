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

namespace Quartz.Impl;

/// <summary>
/// Marks an <see cref="IScheduler" /> that stands for a scheduler living in another process: nothing
/// here shuts it down, and its status is a request rather than a field.
/// </summary>
/// <remarks>
/// <para>
/// Everything a local scheduler answers out of memory, a proxy answers over a network — including the
/// two members <see cref="IScheduler" /> declares as properties, <see cref="IScheduler.Status" /> and
/// <see cref="IScheduler.SchedulerInstanceId" />, which a property can only do by blocking the calling
/// thread until the round trip ends. Code that would otherwise read one of them incidentally — a
/// repository sweeping its entries, a registry building a listing — tests for this first and either
/// asks the asynchronous twin under a deadline of its own or does not ask at all.
/// </para>
/// <para>
/// Internal on purpose: it says nothing a caller could act on, and the two questions it guards are
/// asked in exactly two places in this repository. <c>HttpScheduler</c> is the one implementation.
/// </para>
/// </remarks>
internal interface IProxyScheduler;
