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

    /// <summary>
    /// The cookie the picker writes the selected scheduler's key into, so the next visit opens on it.
    /// </summary>
    internal const string SchedulerCookieName = "qz_scheduler";

    private string? activeSchedulerName;
    private IReadOnlyList<SchedulerHeaderDto> availableSchedulers = [];
    private bool schedulersListed;
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

        string? schedulerCookie = httpContext.Request.Cookies[SchedulerCookieName];
        if (!string.IsNullOrWhiteSpace(schedulerCookie))
        {
            RememberedSchedulerKey = schedulerCookie.Trim();
        }
    }

    public event EventHandler? OnSchedulerChanged;

    /// <summary>
    /// The key the browser remembered from the last visit, or <see langword="null" /> when it remembered
    /// none. Applied once the first listing says whether it is still there: a cookie is the browser's
    /// word, and the listing is what decides which keys this visitor may be pointed at.
    /// </summary>
    public string? RememberedSchedulerKey { get; }

    /// <summary>
    /// The scheduler the dashboard is currently about, as its key: <c>target/name</c>, or the bare name
    /// of a scheduler reached through no target. Assigning one that the last listing did not carry
    /// leaves the previous value in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The listing in <see cref="AvailableSchedulers" /> is always the authorization-filtered one, so it
    /// is the set of keys this visitor may be pointed at. The picker's value, on the other hand, arrives
    /// on a browser <c>change</c> event, and Blazor does not check that such a value was one of the
    /// options the server rendered — so without this the browser could name any scheduler in the process
    /// and every subscribed page would re-read for it, which is what it did before rc.1.
    /// </para>
    /// <para>
    /// Before the first listing there is nothing to check against and the value is taken as given; that is
    /// the dashboard's own start-up assignment, which happens before anything is rendered.
    /// </para>
    /// <para>
    /// Named for the vocabulary <see cref="IQuartzApiClient" /> speaks, whose scheduler-scoped members
    /// take this value: it has been a key since 4.5, and a bare name is the key of a scheduler reached
    /// through no target.
    /// </para>
    /// </remarks>
    public string? ActiveSchedulerName
    {
        get => activeSchedulerName;
        set
        {
            if (schedulersListed && !string.IsNullOrWhiteSpace(value) && Find(value) is null)
            {
                return;
            }

            if (activeSchedulerName != value)
            {
                activeSchedulerName = value;
                OnSchedulerChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Every scheduler the container knows about that the visitor may see, registrations that nothing has
    /// built included.
    /// </summary>
    /// <remarks>
    /// The headers rather than the names, because whether a scheduler exists is the one thing a picker
    /// has to know about a name it is offering: a registration nobody has built has nothing to show, and
    /// omitting it would make the tenant look as if it had never been registered. Every write is a
    /// filtered listing, which is what makes this the set <see cref="ActiveSchedulerName" /> validates
    /// against — recording that a listing happened at all, so that a visitor who passes for no scheduler
    /// gets an empty one rather than an unchecked name.
    /// </remarks>
    public IReadOnlyList<SchedulerHeaderDto> AvailableSchedulers
    {
        get => availableSchedulers;
        set
        {
            availableSchedulers = value;
            schedulersListed = true;
        }
    }

    /// <summary>
    /// The scheduler the dashboard should be about when nothing has chosen one: the one the browser
    /// remembered if the listing still carries it, else the first that exists, falling back to the first
    /// registration, and <see langword="null" /> when there are none.
    /// </summary>
    /// <remarks>
    /// A registration nothing has built has no pages to render, so opening on one would show its
    /// not-created state everywhere while a running scheduler sat further down the list. It is still the
    /// fallback, because a process whose only scheduler has not started is better described by that
    /// scheduler than by nothing at all. A remembered key the listing does not carry — a target that
    /// went away, a cluster whose membership changed — falls through to the same rule.
    /// </remarks>
    public string? DefaultSchedulerName
    {
        get
        {
            if (Find(RememberedSchedulerKey) is { IsCreated: true } remembered)
            {
                return remembered.Key;
            }

            foreach (SchedulerHeaderDto scheduler in AvailableSchedulers)
            {
                if (scheduler.IsCreated)
                {
                    return scheduler.Key;
                }
            }

            return AvailableSchedulers.Count > 0 ? AvailableSchedulers[0].Key : null;
        }
    }

    /// <summary>
    /// What the last listing said about <paramref name="schedulerKey" />, or <see langword="null" />
    /// when it said nothing about it.
    /// </summary>
    /// <remarks>
    /// Null and <c>IsCreated: false</c> are different answers, which is why this returns the header
    /// rather than a flag: a page pointed at a scheduler the listing does not carry must not be told the
    /// scheduler does not exist, while one pointed at a registration nothing has built must. Compared by
    /// key, so that two targets fronting schedulers of one name are two answers.
    /// </remarks>
    public SchedulerHeaderDto? Find(string? schedulerKey)
    {
        if (string.IsNullOrWhiteSpace(schedulerKey))
        {
            return null;
        }

        foreach (SchedulerHeaderDto scheduler in AvailableSchedulers)
        {
            if (string.Equals(scheduler.Key, schedulerKey, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// The instant a wall-clock time names in the selected time zone — what a <c>datetime-local</c> input
    /// means, since it carries no offset of its own.
    /// </summary>
    /// <remarks>
    /// A time the zone skips or repeats at a daylight-saving change reads with the zone's standard offset,
    /// which is <see cref="TimeZoneInfo.GetUtcOffset(DateTime)" />'s answer for both.
    /// </remarks>
    public DateTimeOffset FromSelectedTimeZone(DateTime wallClock)
    {
        TimeZoneInfo timeZone = ResolveSelectedTimeZone();
        DateTime unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
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
