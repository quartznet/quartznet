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

using System.ComponentModel;

namespace Quartz.Util;

/// <summary>
/// A coercion only a <see cref="TypeConverter" /> could make, refused because
/// <see cref="FeatureSwitches.IsTypeConverterCoercionSupported" /> is off.
/// </summary>
/// <remarks>
/// A <see cref="NotSupportedException" />, so a caller that catches one is unaffected. Its own type is what
/// tells <see cref="Impl.PropertySettingJobFactory" /> that the publish, not the data, is what does not fit:
/// a value the untrimmed application converted is refused only because the trimmed one has no
/// <see cref="TypeConverter" />, so the factory reports it even when it ignores mismatches, as the
/// type-name switch's refusals throw for the same kind of misconfiguration.
/// </remarks>
internal sealed class CoercionNotSupportedException : NotSupportedException
{
    /// <param name="requiredType">The type the property takes.</param>
    /// <param name="valueType">The type of the value it was given.</param>
    public CoercionNotSupportedException(Type requiredType, Type valueType)
        : base($"A {valueType} cannot be converted to {requiredType} without a TypeConverter, and this publish does not "
               + $"convert through TypeConverter ({FeatureSwitches.TypeConverterCoercionSwitch} is false). Put the value in the "
               + $"job data as a {requiredType}, or as a string or a number Quartz converts on its own: to a primitive, decimal, "
               + "an enum, Guid, TimeSpan, DateTime, DateTimeOffset or Uri, or the Nullable<T> of one. Set "
               + $"<{FeatureSwitches.TypeConverterCoercionProperty}>true</{FeatureSwitches.TypeConverterCoercionProperty}> "
               + "to convert through TypeConverter again.")
    {
    }
}
