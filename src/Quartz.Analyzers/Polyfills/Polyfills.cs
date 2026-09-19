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

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Quartz;

/// <summary>
/// The members the linked cron sources call that netstandard2.0 does not have, supplied as C# 14
/// extension members so that not one call site changes.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a shape, not a decision: each does what the BCL member of the same name
/// does, and none of them is on the path that decides whether an expression parses. The one
/// exception is <c>TimeZoneInfo.TryConvertIanaIdToWindowsId</c>, which is written below to decline
/// every id — see its own remarks.
/// </para>
/// <para>
/// This class is in the <c>Quartz</c> namespace because that is what puts it in scope for the linked
/// files without a <c>using</c> in any of them: they declare <c>Quartz</c> or <c>Quartz.Util</c>, and
/// an enclosing namespace is searched for extension members.
/// </para>
/// </remarks>
internal static class Polyfills
{
    extension(ArgumentNullException)
    {
        /// <summary>
        /// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)" />, which arrived in .NET 6.
        /// </summary>
        public static void ThrowIfNull(object? argument, [CallerArgumentExpression("argument")] string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }

    extension(char)
    {
        /// <summary>
        /// <see cref="char" />'s <c>IsAsciiDigit</c>, which arrived in .NET 7. Deliberately not
        /// <see cref="char.IsDigit(char)" />: that answers true for every Unicode decimal digit, and a
        /// cron field is written in ASCII.
        /// </summary>
        public static bool IsAsciiDigit(char c) => (uint) (c - '0') <= 9;
    }

    extension(int)
    {
        /// <summary>
        /// <c>int.TryParse</c> over a span, which arrived in .NET Core 2.1.
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> s, out int result)
        {
            return int.TryParse(s.ToString(), out result);
        }

        /// <summary>
        /// <c>int.TryParse</c> over a span, with styles and a format provider.
        /// </summary>
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out int result)
        {
            return int.TryParse(s.ToString(), style, provider, out result);
        }

        /// <summary>
        /// <c>int.Parse</c> over a span, which arrived in .NET Core 2.1.
        /// </summary>
        public static int Parse(ReadOnlySpan<char> s) => int.Parse(s.ToString());
    }

    extension(StringBuilder builder)
    {
        /// <summary>
        /// <see cref="StringBuilder" />'s span <c>Append</c>, which arrived in .NET Core 2.1.
        /// </summary>
        public StringBuilder Append(ReadOnlySpan<char> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                builder.Append(value[i]);
            }

            return builder;
        }
    }

    extension(string)
    {
        /// <summary>
        /// <c>string.Concat(string, ReadOnlySpan&lt;char&gt;)</c>, which arrived in .NET Core 2.1.
        /// </summary>
        public static string Concat(string str0, ReadOnlySpan<char> str1) => str0 + str1.ToString();
    }

    extension(string text)
    {
        /// <summary>
        /// <c>string.Contains(char)</c>, which arrived in .NET Core 2.1.
        /// </summary>
        public bool Contains(char value) => text.IndexOf(value) >= 0;
    }

    extension(TimeZoneInfo)
    {
        /// <summary>
        /// <see cref="TimeZoneInfo" />'s <c>TryConvertIanaIdToWindowsId</c>, which arrived in .NET 6
        /// and declines every id here.
        /// </summary>
        /// <remarks>
        /// The conversion table is the ICU one the BCL carries, and there is none on netstandard2.0.
        /// Declining is the honest answer and costs the analyzer nothing: the only caller is
        /// <c>TimeZones.FindById</c>, which no diagnostic reaches — a cron literal is read with
        /// <c>CronExpression.TryParse</c>, and parsing resolves no time zone. An expression's time
        /// zone is the run-time machine's business anyway, which is why <c>TimeZones.FindById</c> is
        /// on the list of things #3803 deliberately does not check at build time.
        /// </remarks>
        public static bool TryConvertIanaIdToWindowsId(string ianaId, out string? windowsId)
        {
            windowsId = null;
            return false;
        }
    }
}
