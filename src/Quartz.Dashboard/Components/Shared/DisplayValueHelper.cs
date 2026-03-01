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
