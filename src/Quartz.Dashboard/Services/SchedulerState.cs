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

    public event EventHandler? OnSchedulerChanged;

    public string? ActiveSchedulerName
    {
        get => activeSchedulerName;
        set
        {
            if (activeSchedulerName != value)
            {
                activeSchedulerName = value;
                OnSchedulerChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Every scheduler the container knows about, registrations that nothing has built included.
    /// </summary>
    /// <remarks>
    /// The headers rather than the names, because whether a scheduler exists is the one thing a picker
    /// has to know about a name it is offering: a registration nobody has built has nothing to show, and
    /// omitting it would make the tenant look as if it had never been registered.
    /// </remarks>
    public IReadOnlyList<SchedulerHeaderDto> AvailableSchedulers { get; set; } = [];

    /// <summary>
    /// The scheduler the dashboard should be about when nothing has chosen one: the first that exists,
    /// falling back to the first registration, and <see langword="null" /> when there are none.
    /// </summary>
    /// <remarks>
    /// A registration nothing has built has no pages to render, so opening on one would show its
    /// not-created state everywhere while a running scheduler sat further down the list. It is still the
    /// fallback, because a process whose only scheduler has not started is better described by that
    /// scheduler than by nothing at all.
    /// </remarks>
    public string? DefaultSchedulerName
    {
        get
        {
            foreach (SchedulerHeaderDto scheduler in AvailableSchedulers)
            {
                if (scheduler.IsCreated)
                {
                    return scheduler.SchedulerName;
                }
            }

            return AvailableSchedulers.Count > 0 ? AvailableSchedulers[0].SchedulerName : null;
        }
    }

    /// <summary>
    /// What the last listing said about <paramref name="schedulerName" />, or <see langword="null" />
    /// when it said nothing about it.
    /// </summary>
    /// <remarks>
    /// Null and <c>IsCreated: false</c> are different answers, which is why this returns the header
    /// rather than a flag: a page pointed at a scheduler the listing does not carry must not be told the
    /// scheduler does not exist, while one pointed at a registration nothing has built must.
    /// </remarks>
    public SchedulerHeaderDto? Find(string? schedulerName)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            return null;
        }

        foreach (SchedulerHeaderDto scheduler in AvailableSchedulers)
        {
            if (string.Equals(scheduler.SchedulerName, schedulerName, StringComparison.OrdinalIgnoreCase))
            {
                return scheduler;
            }
        }

        return null;
    }

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
        OnSchedulerChanged?.Invoke(this, EventArgs.Empty);
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
