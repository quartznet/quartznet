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

#nullable enable

namespace Quartz.Tests.Unit;

/// <summary>
/// Flips an <see cref="AppContext" /> feature switch for the length of a test, and puts it back.
/// </summary>
/// <remarks>
/// A switch is process-wide, so a fixture that uses this is <c>[NonParallelizable]</c>: the assembly
/// runs fixtures in parallel, and another fixture reading the switch mid-test would see the flipped
/// value. "Back" is <see langword="true" />, which is what an unset switch reads as through
/// <c>FeatureSwitches</c>; there is no way to unset one.
/// </remarks>
internal sealed class FeatureSwitchScope : IDisposable
{
    private readonly string name;

    public FeatureSwitchScope(string name, bool enabled)
    {
        this.name = name;
        AppContext.SetSwitch(name, enabled);
    }

    public void Dispose()
    {
        AppContext.SetSwitch(name, true);
    }
}
