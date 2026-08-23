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

using Quartz.Core;

namespace Quartz.Impl;

/// <inheritdoc />
/// <remarks>
/// A service rather than a static property so that a component reading the firing says so in its
/// constructor and can be handed something else in a test. The state it reads is process-wide because
/// it belongs to the asynchronous flow, not to a container — see <see cref="AmbientJobExecution" />.
/// </remarks>
internal sealed class JobExecutionContextAccessor : IJobExecutionContextAccessor
{
    /// <inheritdoc />
    public IJobExecutionContext? Current => AmbientJobExecution.Current;
}
