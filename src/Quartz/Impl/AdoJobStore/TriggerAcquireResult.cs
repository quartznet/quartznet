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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// One candidate returned by <see cref="IDriverDelegate.SelectTriggersToAcquire" />.
/// </summary>
/// <remarks>
/// Deliberately not the trigger itself: acquisition looks at many more rows than it takes, so the
/// candidate carries only what deciding costs — the key to reserve, the job type to load, and the
/// execution group to count against its limit.
/// </remarks>
/// <param name="TriggerKey">The key of the trigger that could be fired.</param>
/// <param name="JobTypeName">The name of the job's type, as the trigger's job row stores it — the same
/// thing <see cref="JobHeader.JobTypeName" /> carries, and what the type loader is handed to load
/// the job. Not to be confused with <see cref="TriggerHeader.TriggerType" />, which is a store
/// discriminator rather than a type name.</param>
/// <param name="ExecutionGroup">The trigger's execution group, if it has one.</param>
public readonly record struct TriggerAcquireResult(TriggerKey TriggerKey, string JobTypeName, string? ExecutionGroup);
