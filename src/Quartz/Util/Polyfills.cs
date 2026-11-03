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

#if NETFRAMEWORK || NETSTANDARD2_0
using System.Runtime.CompilerServices;
#endif

namespace Quartz.Util;

// BCL members the oldest targets lack, supplied as C# 14 extension members so that a call site is
// written as it would be on .NET alone. A real member always wins over an extension member, so on the
// targets that have one the BCL's binds and these blocks are empty. A static extension member's
// receiver type is erased in the lowered signature, so each receiver type gets its own class.

/// <summary>
/// <see cref="double" /> members the oldest targets lack.
/// </summary>
internal static class DoublePolyfills
{
    extension(double)
    {
#if NETFRAMEWORK || NETSTANDARD2_0
        // double.IsFinite arrived in .NET Core 2.1 / netstandard2.1.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
#endif
    }
}

/// <summary>
/// <see cref="float" /> members the oldest targets lack.
/// </summary>
internal static class SinglePolyfills
{
    extension(float)
    {
#if NETFRAMEWORK || NETSTANDARD2_0
        // float.IsFinite arrived in .NET Core 2.1 / netstandard2.1.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
#endif
    }
}
