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

namespace Quartz.Util;

/// <summary>
/// The one member of <c>Quartz.Util.QuartzEnvironment</c> the linked cron sources read.
/// </summary>
/// <remarks>
/// The shipped file also reads two environment variables and logs what it finds, which is a
/// <c>Microsoft.Extensions.Logging</c> reference an analyzer has no use for. The property itself is a
/// single expression and is copied verbatim.
/// </remarks>
internal static class QuartzEnvironment
{
    /// <summary>
    /// Whether the runtime is Mono, which needs the <see cref="DateTime" /> overload of
    /// <c>GetUtcOffset</c> rather than the <see cref="DateTimeOffset" /> one.
    /// </summary>
    internal static bool IsRunningOnMono { get; } = Type.GetType("Mono.Runtime") is not null;
}
