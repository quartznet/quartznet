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
using System.Reflection;
using System.Text.Json;

namespace Quartz.Dashboard.Components.Shared;

internal static class DisplayValueHelper
{
    private static readonly JsonSerializerOptions JobDataMapOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web).AddQuartzConverters(newtonsoftCompatibilityMode: false);

    public static object? GetObject(object? source, params string[] paths)
    {
        if (source is null)
        {
            return null;
        }

        foreach (string path in paths)
        {
            if (TryGetPathValue(source, path, out object? value))
            {
                return value;
            }
        }

        return null;
    }

    public static JobDataMap? GetJobDataMap(object? source, params string[] paths)
    {
        object? value = GetObject(source, paths);
        if (value is JobDataMap dataMap)
        {
            return dataMap;
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return element.Deserialize<JobDataMap>(JobDataMapOptions);
        }

        return null;
    }

    public static string GetString(object? source, params string[] paths)
    {
        object? value = GetObject(source, paths);
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string str)
        {
            return str;
        }

        if (value is JsonElement element)
        {
            return element.ToString();
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    public static DateTimeOffset? GetDateTimeOffset(object? source, params string[] paths)
    {
        object? value = GetObject(source, paths);
        if (value is null)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(dateTime);
        }

        if (value is string stringValue &&
            DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(jsonElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset jsonParsed))
            {
                return jsonParsed;
            }
        }

        return null;
    }

    public static IReadOnlyList<object> ToObjectList<T>(IReadOnlyList<T> source)
    {
        List<object> result = new(source.Count);
        foreach (T item in source)
        {
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    public static string FormatKey(string? group, string? name)
    {
        string safeGroup = string.IsNullOrWhiteSpace(group) ? "DEFAULT" : group;
        string safeName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        return safeGroup + "." + safeName;
    }

    /// <summary>
    /// A key cut into the pieces a listing may break it between, which together are the key exactly: after a
    /// separator (<c>.</c>, <c>_</c>, <c>-</c>, <c>/</c> or a space), so it stays on the line it ends; before an
    /// upper-case letter that follows a lower-case letter or a digit; and before the last capital of an upper-case
    /// run that a lower-case letter follows, so <c>XMLParserJob</c> is <c>XML</c>, <c>Parser</c>, <c>Job</c>.
    /// </summary>
    /// <remarks>
    /// A word longer than its column still breaks, wherever the line runs out: the stylesheet's
    /// <c>overflow-wrap: anywhere</c> is the last resort, after these.
    /// </remarks>
    public static List<string> KeyLineSegments(string key)
    {
        List<string> segments = [];
        int start = 0;
        for (int i = 1; i < key.Length; i++)
        {
            char previous = key[i - 1];
            char current = key[i];
            bool afterSeparator = previous is '.' or '_' or '-' or '/' or ' ';
            bool wordStart = char.IsUpper(current)
                             && (char.IsLower(previous)
                                 || char.IsDigit(previous)
                                 || (char.IsUpper(previous) && i + 1 < key.Length && char.IsLower(key[i + 1])));

            if (afterSeparator || wordStart)
            {
                segments.Add(key[start..i]);
                start = i;
            }
        }

        segments.Add(key[start..]);
        return segments;
    }

    /// <summary>
    /// A duration as a clock, <c>hh:mm:ss</c>, with its days in front from a day up:
    /// <c>1.02:00:00</c> for 26 hours.
    /// </summary>
    /// <remarks>
    /// The format <c>hh\:mm\:ss</c> alone writes the hours component, so a 26-hour run read as
    /// <c>02:00:00</c>.
    /// </remarks>
    public static string FormatClock(TimeSpan duration)
    {
        string format = duration.Duration() < TimeSpan.FromDays(1) ? @"hh\:mm\:ss" : @"d\.hh\:mm\:ss";
        return duration.ToString(format, CultureInfo.InvariantCulture);
    }

    private static bool TryGetPathValue(object source, string path, out object? value)
    {
        value = source;
        string[] parts = path.Split('.');
        foreach (string part in parts)
        {
            if (!TryGetMemberValue(value, part, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetMemberValue(object? source, string memberName, out object? value)
    {
        value = null;
        if (source is null)
        {
            return false;
        }

        if (source is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (jsonElement.TryGetProperty(memberName, out JsonElement property))
            {
                value = property;
                return true;
            }

            foreach (JsonProperty candidate in jsonElement.EnumerateObject())
            {
                if (!string.Equals(candidate.Name, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = candidate.Value;
                return true;
            }

            return false;
        }

        Type sourceType = source.GetType();
        PropertyInfo? propertyInfo = sourceType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (propertyInfo is not null)
        {
            value = propertyInfo.GetValue(source);
            return true;
        }

        return false;
    }
}
