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

using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// What Quartz reflects over on a job type, spelled once so that every API which accepts one asks for
/// the same thing.
/// </summary>
/// <remarks>
/// <para>
/// Three sets, and each is something Quartz already does today:
/// <see cref="DynamicallyAccessedMemberTypes.PublicConstructors" /> because a job factory activates the
/// type, <see cref="DynamicallyAccessedMemberTypes.PublicProperties" /> because
/// <see cref="Impl.PropertySettingJobFactory" /> pushes the <see cref="JobDataMap" /> onto its
/// properties, and <see cref="DynamicallyAccessedMemberTypes.Interfaces" /> because
/// <see cref="DisallowConcurrentExecutionAttribute" /> and
/// <see cref="PersistJobDataAfterExecutionAttribute" /> are inherited from an interface as readily as
/// from a base class.
/// </para>
/// <para>
/// <see cref="DynamicallyAccessedMemberTypes.PublicMethods" /> is deliberately absent. It was on the
/// job-type parameters until issue #3341, step 3, and nothing needed it: a kept property keeps its
/// accessors, and <see cref="IJob.Execute" /> is reached through the interface rather than by name.
/// </para>
/// </remarks>
internal static class JobTypeMembers
{
    /// <summary>
    /// The members a job type must keep for Quartz to be able to run it.
    /// </summary>
    public const DynamicallyAccessedMemberTypes Required =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;
}
