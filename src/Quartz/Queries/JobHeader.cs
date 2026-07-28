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
/// A job listing entry: the job's metadata without its <see cref="JobDataMap" />,
/// so that listing jobs never loads or deserializes job data.
/// </summary>
/// <param name="Key">The job's key.</param>
/// <param name="Description">The job's description, if one was given.</param>
/// <param name="JobTypeName">The job type's name as the store recorded it; the type itself
/// is not loaded for a listing.</param>
/// <param name="Durable">Whether the job survives having no triggers.</param>
/// <param name="ConcurrentExecutionDisallowed">Whether concurrent executions are disallowed.</param>
/// <param name="PersistJobDataAfterExecution">Whether job data is persisted after execution.</param>
/// <param name="RequestsRecovery">Whether the job requests recovery after a scheduler crash.</param>
public sealed record JobHeader(
    JobKey Key,
    string? Description,
    string JobTypeName,
    bool Durable,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    bool RequestsRecovery);
