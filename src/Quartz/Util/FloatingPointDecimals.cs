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
using System.Globalization;

namespace Quartz.Util;

/// <summary>
/// Converts a stored number to <see cref="decimal" /> the way every runtime before .NET 11 did.
/// </summary>
/// <remarks>
/// .NET 11 converts a <see cref="double" /> or a <see cref="float" /> to the decimal nearest its exact
/// binary value, so <c>12.34d</c> becomes <c>12.339999999999999857891452848m</c>. Before, a double kept 15
/// significant digits and a float 7, which gave back <c>12.34m</c>. The <c>DEC_PROP</c> columns hold
/// amounts a person typed, and reading one back must not give a different number after a runtime
/// upgrade. So a double or a float goes through the rounding the
/// <see href="https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions">breaking-change page</see>
/// gives: format with <c>G15</c> or <c>G7</c>, parse that as a decimal. It allocates nothing, except on
/// .NET Framework and netstandard2.0, which format to a short string because they have no span to format
/// into. A value outside decimal's range still throws <see cref="OverflowException" />, as the conversion did.
/// </remarks>
internal static class FloatingPointDecimals
{
    /// <summary>
    /// <paramref name="value" /> as a decimal: a double or a float rounded to the significant digits it
    /// carries, anything else by <see cref="Convert.ToDecimal(object, IFormatProvider)" />.
    /// </summary>
    public static decimal ToDecimal(object? value) => value switch
    {
        double d => FromDouble(d),
        float f => FromSingle(f),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// <paramref name="value" /> rounded to 15 significant digits.
    /// </summary>
    public static decimal FromDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            // Throws the OverflowException the conversion always threw; parsing "NaN" would throw a
            // FormatException instead.
            return Convert.ToDecimal(value);
        }

#if NETFRAMEWORK || NETSTANDARD2_0
        // Span<T>, double.TryFormat and decimal.Parse(ReadOnlySpan<char>) arrived in .NET Core 2.1, and
        // net462 has no System.Memory. The same format rounds the same way.
        return decimal.Parse(value.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out int length, "G15", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
    }

    /// <summary>
    /// <paramref name="value" /> rounded to 7 significant digits.
    /// </summary>
    public static decimal FromSingle(float value)
    {
        if (!float.IsFinite(value))
        {
            return Convert.ToDecimal(value);
        }

#if NETFRAMEWORK || NETSTANDARD2_0
        // As in FromDouble.
        return decimal.Parse(value.ToString("G7", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
#else
        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out int length, "G7", CultureInfo.InvariantCulture);
        return decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
#endif
    }
}
