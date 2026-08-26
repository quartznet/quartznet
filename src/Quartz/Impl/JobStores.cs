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

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// The one place that answers "what store is this, really".
/// </summary>
/// <remarks>
/// <para>
/// A scheduler's store is very often not the object that stores anything. Quartz wraps every store it
/// builds in a tracing decorator, and an application is invited to wrap it in more of its own —
/// <see cref="DelegatingJobStore" /> exists to make that easy. So a bare <c>is AdoJobStoreBase</c> at a
/// call site that wants to know whether scheduling data is in a database gets the wrong answer as soon
/// as anything is layered over it, and the failure is silent: an ambient transaction stops being
/// honoured, a shared-database warning stops being issued, a scheduler reports its store's type as a
/// decorator's.
/// </para>
/// <para>
/// Every such test goes through <see cref="Unwrap" />. This is deliberately only for questions about
/// <em>identity</em> — what kind of store it is, what database it talks to. A question about
/// <em>behaviour</em> — <see cref="IJobStore.SupportsPersistence" />, <see cref="IJobStore.Clustered" />,
/// or any operation — is asked of the outermost store and forwarded down, because that is the whole
/// point of letting an application decorate one.
/// </para>
/// </remarks>
internal static class JobStores
{
    /// <summary>
    /// The store underneath however many <see cref="DelegatingJobStore" /> layers are in the way.
    /// </summary>
    /// <param name="jobStore">The store as the scheduler holds it, decorators and all.</param>
    /// <returns>
    /// The innermost store. A store that is not a <see cref="DelegatingJobStore" /> is its own answer,
    /// so this is the identity function in the common case.
    /// </returns>
    internal static IJobStore Unwrap(IJobStore jobStore)
    {
        while (jobStore is DelegatingJobStore delegating)
        {
            jobStore = delegating.Inner;
        }

        return jobStore;
    }
}
