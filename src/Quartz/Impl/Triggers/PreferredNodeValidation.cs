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

using System;

namespace Quartz.Impl.Triggers;

/// <summary>
/// Shared validation/normalization for user-supplied preferred node (node affinity)
/// values. Keeps the reserved-token rule identical across every entry path
/// (<see cref="Quartz.TriggerBuilder"/>, <see cref="AbstractTrigger"/>, and
/// <see cref="Quartz.TriggerDetailsUpdate"/>).
/// </summary>
internal static class PreferredNodeValidation
{
    /// <summary>
    /// Prefix used internally to mark auto-pinned triggers (<c>"auto:nodeA"</c>). Reserved:
    /// user-supplied preferred node values may not contain it.
    /// </summary>
    internal const string AutoPinPrefix = "auto:";

    /// <summary>
    /// Normalizes a user-supplied preferred node value: blank (null/empty/whitespace)
    /// becomes <see langword="null"/>, otherwise the trimmed value. Throws
    /// <see cref="ArgumentException"/> when the value contains the reserved
    /// <see cref="AutoPinPrefix"/> substring.
    /// </summary>
    internal static string? NormalizeUserValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value!.Trim();
        if (trimmed.IndexOf(AutoPinPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new ArgumentException(
                "The 'auto:' substring is reserved for internal auto-pin tracking. " +
                "Use \"*\" for auto-pin or a plain node name for explicit pin.",
                paramName);
        }

        return trimmed;
    }
}
