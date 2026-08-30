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

namespace Quartz.Extensibility;

/// <summary>
/// Turns an <see cref="IJob{TInput}" />'s input into the string the scheduler stores under
/// <see cref="SchedulerConstants.JobInput" />, and back again when the job runs.
/// </summary>
/// <remarks>
/// <para>
/// This is a different job from <see cref="IObjectSerializer" />'s. That one owns the store's own
/// shapes — triggers, calendars, the job data map itself — and answers a persistent job store. This one
/// owns a single application-defined payload, is asked for it by the scheduler on the way in and by the
/// job on the way out, and is used whichever store the scheduler runs on, including the in-memory one.
/// </para>
/// <para>
/// A string rather than bytes, for the reason <see cref="SchedulerConstants.JobInput" /> gives: it is
/// what every path the value can take carries unchanged.
/// </para>
/// <para>
/// The interface carries no trimming annotations, exactly as <see cref="IObjectSerializer" /> carries
/// none. An annotation on an interface member has to be repeated by every implementation or the
/// implementation reports IL2046, and an implementation written outside Quartz cannot be made to.
/// </para>
/// </remarks>
/// <seealso cref="Quartz.Impl.SystemTextJsonJobInputSerializer" />
public interface IJobInputSerializer
{
    /// <summary>
    /// Serializes a job's input for storage.
    /// </summary>
    /// <param name="input">The input to serialize, always non-null.</param>
    string Serialize(object input);

    /// <summary>
    /// Deserializes a job's input from what <see cref="Serialize" /> stored.
    /// </summary>
    /// <param name="payload">The stored payload, always non-null.</param>
    TInput? Deserialize<TInput>(string payload);
}
