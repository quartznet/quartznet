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
/// What is known about a job's two attribute-derived flags without going looking for its type.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IJobDetail.ConcurrentExecutionDisallowed" /> and
/// <see cref="IJobDetail.PersistJobDataAfterExecution" /> are effective values: they answer
/// <see langword="false" /> for a job whose type this process cannot resolve, because a reader has to be
/// told something. A projection onto a wire has a third answer available — say nothing — and it is the
/// right one, because the reader on the other side may well hold the assembly this one does not.
/// </para>
/// <para>
/// Only <see cref="JobDetailImpl" /> tracks the difference; an <see cref="IJobDetail" /> of an
/// application's own has a <see langword="bool" /> and nothing more to say, so its answer is taken as
/// stated.
/// </para>
/// </remarks>
internal static class JobDetailFlags
{
    /// <inheritdoc cref="JobDetailFlags" />
    public static bool? ConcurrentExecutionDisallowed(IJobDetail jobDetail)
    {
        return jobDetail is JobDetailImpl impl
            ? impl.StatedConcurrentExecutionDisallowed
            : jobDetail.ConcurrentExecutionDisallowed;
    }

    /// <inheritdoc cref="JobDetailFlags" />
    public static bool? PersistJobDataAfterExecution(IJobDetail jobDetail)
    {
        return jobDetail is JobDetailImpl impl
            ? impl.StatedPersistJobDataAfterExecution
            : jobDetail.PersistJobDataAfterExecution;
    }
}
