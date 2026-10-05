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

namespace Quartz.Configuration;

/// <summary>
/// A scheduler's default retry policy, as <c>UseDefaultRetryPolicy</c> set it.
/// </summary>
/// <remarks>
/// Options of their own rather than a member of <see cref="QuartzSchedulerOptions" />, which is bound
/// from the <c>Scheduler</c> configuration section by generated code: a <see cref="RetryPolicy" /> has no
/// public constructor, so that binder cannot build one, and a string member there would be a second
/// spelling of the same setting. Internal, because the builder method is the one way to set it.
/// </remarks>
internal sealed class SchedulerRetryOptions
{
    /// <summary>
    /// The policy a failed firing is retried under when neither its trigger nor its job type names one.
    /// </summary>
    public RetryPolicy? DefaultPolicy { get; set; }
}
