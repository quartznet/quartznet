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

namespace Quartz;

/// <summary>
/// Lists the schedulers a container knows about, whether or not any of them has been created.
/// </summary>
/// <remarks>
/// <para>
/// This is the read that distinguishes <em>registered</em> from <em>running</em>.
/// <see cref="Extensibility.ISchedulerRepository" /> holds scheduler <em>instances</em>, so
/// <c>LookupAll()</c> and <see cref="ISchedulerFactory.GetAllSchedulers" /> can only report the
/// schedulers something already asked for — under a multi-tenant registration that means an operator
/// cannot enumerate tenants without starting every one of them. This one reads the registrations, and
/// says of each whether a scheduler exists behind it.
/// </para>
/// <para>
/// Registered by <c>AddQuartz</c>, so any container with Quartz in it has one. It answers for the
/// container it belongs to and no other; a second container in the same process has its own.
/// </para>
/// </remarks>
public interface ISchedulerRegistry
{
    /// <summary>
    /// Returns every scheduler this container has registered, plus every scheduler bound into its
    /// repository that no registration accounts for, ordered by name.
    /// </summary>
    /// <remarks>
    /// Nothing is created by asking: a registration that has never been resolved is reported with a
    /// <see langword="null" /> <see cref="SchedulerRegistration.Status" /> rather than built.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default);
}
