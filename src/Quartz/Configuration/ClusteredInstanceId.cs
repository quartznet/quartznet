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
/// Gives a clustered scheduler an instance id of its own, unless the application chose one.
/// </summary>
/// <remarks>
/// <para>
/// A cluster node recognises its own check-in row and its own fired triggers by
/// <see cref="QuartzSchedulerOptions.InstanceId" />, and every scheduler starts with
/// <see cref="QuartzSchedulerOptions.DefaultInstanceId" />. Two nodes that asked for clustering and
/// nothing else therefore shared one id: each read the other's fired triggers as its own, neither saw
/// the other fail, and a restarted node's recovery replayed its live peer's firings (#4091).
/// </para>
/// <para>
/// Applied as a post-configure step, so it sees every <c>Configure</c> the application made — before
/// or after <c>UseClustering()</c>, in code, in configuration or as a flat key. An explicit
/// <c>InstanceId</c>, or <c>GenerateInstanceId</c> already on (which choosing a generator sets), is left
/// alone. A <c>GenerateInstanceId</c> set to <see langword="false" /> cannot be told from the default;
/// a fixed id is spelled by setting <c>InstanceId</c>.
/// </para>
/// <para>
/// Turning generation on for a store that turns out not to be clustered changes nothing:
/// <c>DefaultSchedulerFactory</c> asks the generator only when the store reports itself clustered.
/// </para>
/// </remarks>
internal static class ClusteredInstanceId
{
    /// <summary>
    /// Turns <see cref="QuartzSchedulerOptions.GenerateInstanceId" /> on when the id is still the default.
    /// </summary>
    public static void GenerateUnlessChosen(QuartzSchedulerOptions options)
    {
        if (!options.GenerateInstanceId
            && string.Equals(options.InstanceId, QuartzSchedulerOptions.DefaultInstanceId, StringComparison.Ordinal))
        {
            options.GenerateInstanceId = true;
        }
    }
}
