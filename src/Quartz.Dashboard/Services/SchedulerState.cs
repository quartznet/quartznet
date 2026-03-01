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

using Microsoft.AspNetCore.Http;

namespace Quartz.Dashboard.Services;

internal sealed class SchedulerState
{
    private const string themeCookieName = "qz_theme";
    private const string timeZoneCookieName = "qz_tz";

    private string? activeSchedulerName;
    private string selectedTimeZoneId = TimeZoneInfo.Local.Id;
    private string selectedTheme = "system";

    public SchedulerState(IHttpContextAccessor httpContextAccessor)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        string? themeCookie = httpContext.Request.Cookies[themeCookieName];
        if (!string.IsNullOrWhiteSpace(themeCookie))
        {
            selectedTheme = NormalizeTheme(themeCookie);
        }

        string? timeZoneCookie = httpContext.Request.Cookies[timeZoneCookieName];
        if (!string.IsNullOrWhiteSpace(timeZoneCookie))
        {
            selectedTimeZoneId = NormalizeTimeZoneId(timeZoneCookie);
        }
    }

#pragma warning disable MA0046
    public event Action? OnSchedulerChanged;
#pragma warning restore MA0046

    public string? ActiveSchedulerName
    {
        get => activeSchedulerName;
        set
        {
            if (activeSchedulerName != value)
            {
                activeSchedulerName = value;
                OnSchedulerChanged?.Invoke();
            }
        }
    }

    public IReadOnlyList<string> AvailableSchedulers { get; set; } = [];

    public string SelectedTimeZoneId
    {
        get => selectedTimeZoneId;
        set
        {
            string normalized = NormalizeTimeZoneId(value);
            selectedTimeZoneId = normalized;
        }
    }

    public string SelectedTheme
    {
        get => selectedTheme;
        set
        {
            string normalized = NormalizeTheme(value);
            selectedTheme = normalized;
        }
    }

    public DateTimeOffset ConvertToSelectedTimeZone(DateTimeOffset value)
    {
        TimeZoneInfo timeZone = ResolveSelectedTimeZone();
        return TimeZoneInfo.ConvertTime(value, timeZone);
    }

    public string FormatInSelectedTimeZone(DateTimeOffset value, string format = "u")
    {
        DateTimeOffset converted = ConvertToSelectedTimeZone(value);
        string outputFormat = string.Equals(format, "u", StringComparison.Ordinal)
            ? "yyyy-MM-dd HH:mm:ss zzz"
            : format;
        return converted.ToString(outputFormat, CultureInfo.InvariantCulture);
    }

    public string FormatInSelectedTimeZone(DateTimeOffset? value, string format = "u")
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        return FormatInSelectedTimeZone(value.Value, format);
    }

    public void NotifyChanged()
    {
        OnSchedulerChanged?.Invoke();
    }

    private TimeZoneInfo ResolveSelectedTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(selectedTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static string NormalizeTimeZoneId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeZoneInfo.Local.Id;
        }

        string candidate = value.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(candidate);
            return candidate;
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local.Id;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local.Id;
        }
    }

    private static string NormalizeTheme(string? value)
    {
        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
        {
            return "light";
        }

        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return "dark";
        }

        return "system";
    }
}
