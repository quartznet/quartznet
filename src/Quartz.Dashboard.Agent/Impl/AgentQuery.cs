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

using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// The query string of one request down the tunnel, read the way ASP.NET Core's minimal APIs bind a
/// handler's parameters from it: by name, ignoring case, <c>+</c> as a space, percent-escapes undone, and
/// a value that does not parse as the type the parameter has refused as a <c>400</c>.
/// </summary>
internal sealed class AgentQuery
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);

    private AgentQuery()
    {
    }

    /// <summary>
    /// The query of <paramref name="path" /> — what follows its first <c>?</c> — or an empty one.
    /// </summary>
    public static AgentQuery Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        AgentQuery query = new();
        int start = path.IndexOf('?', StringComparison.Ordinal);
        if (start < 0 || start == path.Length - 1)
        {
            return query;
        }

        foreach (string pair in path.AsSpan(start + 1).ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string name = Decode(equals < 0 ? pair : pair[..equals]);
            string value = equals < 0 ? string.Empty : Decode(pair[(equals + 1)..]);

            if (!query.values.TryGetValue(name, out List<string>? list))
            {
                query.values[name] = list = [];
            }

            list.Add(value);
        }

        return query;
    }

    /// <summary>
    /// The first value given for <paramref name="name" />, or <see langword="null" /> when none was.
    /// </summary>
    public string? Get(string name)
    {
        return values.TryGetValue(name, out List<string>? list) ? list[0] : null;
    }

    /// <summary>
    /// Every value given for <paramref name="name" />, or <see langword="null" /> when none was.
    /// </summary>
    public string[]? GetAll(string name)
    {
        return values.TryGetValue(name, out List<string>? list) ? list.ToArray() : null;
    }

    public int GetInt(string name, int fallback)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw Unreadable(name, value);
    }

    public bool GetBool(string name, bool fallback)
    {
        return GetNullableBool(name) ?? fallback;
    }

    public bool? GetNullableBool(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return bool.TryParse(value, out bool parsed) ? parsed : throw Unreadable(name, value);
    }

    public DateTimeOffset? GetDateTimeOffset(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : throw Unreadable(name, value);
    }

    public TimeSpan? GetTimeSpan(string name)
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed) ? parsed : throw Unreadable(name, value);
    }

    public TEnum? GetEnum<TEnum>(string name) where TEnum : struct, Enum
    {
        string? value = Get(name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed) ? parsed : throw Unreadable(name, value);
    }

    /// <summary>
    /// What ASP.NET Core answers for a parameter it cannot bind, under the name the wire has always used.
    /// </summary>
    private static InvalidRequestException Unreadable(string name, string value)
    {
        return new InvalidRequestException($"Failed to bind parameter \"{name}\" from \"{value}\".");
    }

    private static string Decode(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
