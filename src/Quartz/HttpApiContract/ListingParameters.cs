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

namespace Quartz.HttpApiContract;

/// <summary>
/// What a listing or group-matcher request asked for: its page, its group and name matchers, and — for
/// fire instances — its state.
/// </summary>
/// <remarks>
/// <para>
/// One reading for the twelve routes that take these, so the rules are stated once: the page size a
/// carrier allows, one matcher per filter, and <c>state=Any</c>. The paging and the state are read by
/// <see cref="Read" />, which a carrier calls before it looks a scheduler up, so a request with a
/// malformed page is refused as one whatever scheduler it names. The matchers are read where an
/// operation applies them, after the lookup, which is the order the routes have always answered in.
/// </para>
/// <para>
/// A group-matcher mutation pages nothing and is read by <see cref="Groups" />.
/// </para>
/// </remarks>
internal sealed record ListingParameters
{
    private ListingParameters()
    {
    }

    /// <summary>
    /// A group-matcher mutation's matchers, which name no page.
    /// </summary>
    public static ListingParameters Groups(string? contains, string? endsWith, string? startsWith, string? equals)
    {
        return new ListingParameters
        {
            GroupContains = contains,
            GroupEndsWith = endsWith,
            GroupStartsWith = startsWith,
            GroupEquals = equals
        };
    }

    /// <summary>
    /// Reads a listing's paging and, for a fire-instance listing, its state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="take" /> is a string so that <c>all</c> can mean <see cref="PagedQuery.All" />.
    /// Asking for everything is a real thing to want — an export, a group-name list, a migration — and
    /// the number behind it is <c>2147483647</c>, which reads in a URL as a mistake rather than as an
    /// intention. The number is still accepted.
    /// </para>
    /// <para>
    /// <paramref name="maxPageSize" /> bounds both spellings and answers them differently on purpose. A
    /// number is a request the server can meet or cannot, so one above the cap is refused naming it.
    /// <c>all</c> says "as many as you will give me", so it is answered with the cap, and <c>hasMore</c>
    /// says whether that was all of them — which is what every <c>SchedulerQueryExtensions</c> listing asks
    /// for through <c>HttpScheduler</c>, three matches or three million. Zero is no cap.
    /// </para>
    /// <para>
    /// A fire-instance <paramref name="state" /> is read here rather than bound as a nullable enum,
    /// because null already means something on the query record — every state — and an unnamed parameter
    /// must instead mean "whatever the record defaults to". <c>Any</c> is every state.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidRequestException">
    /// <paramref name="skip" /> is negative; <paramref name="take" /> is negative, is neither a number nor
    /// <c>all</c>, or is a number above <paramref name="maxPageSize" />; or <paramref name="state" /> names
    /// no fire-instance state.
    /// </exception>
    public static ListingParameters Read(int skip, string? take, bool includeTotalCount, int maxPageSize, string? state = null)
    {
        if (skip < 0)
        {
            throw new InvalidRequestException("skip must not be negative");
        }

        return new ListingParameters
        {
            Skip = skip,
            Take = ReadTake(take, maxPageSize),
            IncludeTotalCount = includeTotalCount,
            StateNamed = state is not null,
            State = ReadState(state)
        };
    }

    public int Skip { get; private init; }

    /// <summary>
    /// The page size to apply, or <see langword="null" /> when the request named none and the query
    /// record's own default stands.
    /// </summary>
    public int? Take { get; private init; }

    public bool IncludeTotalCount { get; private init; }

    /// <summary>
    /// Whether the request named a fire-instance state at all. One that did not gets the query record's
    /// default, which is <see cref="FireInstanceState.Executing" /> rather than every state.
    /// </summary>
    public bool StateNamed { get; private init; }

    /// <summary>
    /// The fire-instance state asked for, <see langword="null" /> meaning every state.
    /// </summary>
    public FireInstanceState? State { get; private init; }

    public string? GroupContains { get; init; }
    public string? GroupEndsWith { get; init; }
    public string? GroupStartsWith { get; init; }
    public string? GroupEquals { get; init; }

    public string? NameContains { get; init; }
    public string? NameEndsWith { get; init; }
    public string? NameStartsWith { get; init; }
    public string? NameEquals { get; init; }

    /// <summary>
    /// <paramref name="query" /> with this request's page applied.
    /// </summary>
    public TQuery Page<TQuery>(TQuery query) where TQuery : PagedQuery
    {
        PagedQuery paged = ((PagedQuery) query) with { Skip = Skip, IncludeTotalCount = IncludeTotalCount };

        // a request that names no take gets the query record's own default page size
        if (Take.HasValue)
        {
            paged = paged with { Take = Take.Value };
        }

        return (TQuery) paged;
    }

    /// <summary>
    /// The group filter the request asked for, which is "any group" when it asked for none.
    /// </summary>
    /// <exception cref="InvalidRequestException">More than one group matcher was given.</exception>
    public GroupMatcher<T> GroupFilter<T>() where T : Key<T>
    {
        RequireOneRule(GroupContains, GroupEndsWith, GroupStartsWith, GroupEquals);

        if (!string.IsNullOrWhiteSpace(GroupContains))
        {
            return GroupMatcher<T>.GroupContains(GroupContains);
        }

        if (!string.IsNullOrWhiteSpace(GroupEndsWith))
        {
            return GroupMatcher<T>.GroupEndsWith(GroupEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(GroupStartsWith))
        {
            return GroupMatcher<T>.GroupStartsWith(GroupStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(GroupEquals))
        {
            return GroupMatcher<T>.GroupEquals(GroupEquals);
        }

        return GroupMatcher<T>.AnyGroup();
    }

    /// <summary>
    /// The name filter the request asked for, or null when it asked for none — a name filter is
    /// optional, where the group filter always ends up as "any group".
    /// </summary>
    /// <exception cref="InvalidRequestException">More than one name matcher was given.</exception>
    public NameMatcher<T>? NameFilter<T>() where T : Key<T>
    {
        RequireOneRule(NameContains, NameEndsWith, NameStartsWith, NameEquals);

        if (!string.IsNullOrWhiteSpace(NameContains))
        {
            return NameMatcher<T>.NameContains(NameContains);
        }

        if (!string.IsNullOrWhiteSpace(NameEndsWith))
        {
            return NameMatcher<T>.NameEndsWith(NameEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(NameStartsWith))
        {
            return NameMatcher<T>.NameStartsWith(NameStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(NameEquals))
        {
            return NameMatcher<T>.NameEquals(NameEquals);
        }

        return null;
    }

    /// <summary>
    /// The counterpart of <see cref="NameFilter{T}" /> for the listings whose subject is named rather
    /// than keyed — calendars and groups — so their filter is a <see cref="NameMatcher" />, spelled the
    /// same way on the wire.
    /// </summary>
    /// <exception cref="InvalidRequestException">More than one name matcher was given.</exception>
    public NameMatcher? NameFilter()
    {
        RequireOneRule(NameContains, NameEndsWith, NameStartsWith, NameEquals);

        if (!string.IsNullOrWhiteSpace(NameContains))
        {
            return NameMatcher.NameContains(NameContains);
        }

        if (!string.IsNullOrWhiteSpace(NameEndsWith))
        {
            return NameMatcher.NameEndsWith(NameEndsWith);
        }

        if (!string.IsNullOrWhiteSpace(NameStartsWith))
        {
            return NameMatcher.NameStartsWith(NameStartsWith);
        }

        if (!string.IsNullOrWhiteSpace(NameEquals))
        {
            return NameMatcher.NameEquals(NameEquals);
        }

        return null;
    }

    private static int? ReadTake(string? take, int maxPageSize)
    {
        if (string.IsNullOrWhiteSpace(take))
        {
            return null;
        }

        if (string.Equals(take, HttpApiConstants.AllItems, StringComparison.OrdinalIgnoreCase))
        {
            return maxPageSize > 0 ? maxPageSize : PagedQuery.All;
        }

        if (!int.TryParse(take, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new InvalidRequestException(
                $"take must be a number or '{HttpApiConstants.AllItems}', which asks for every match");
        }

        if (parsed < 0)
        {
            throw new InvalidRequestException("take must not be negative");
        }

        if (maxPageSize > 0 && parsed > maxPageSize)
        {
            throw new InvalidRequestException(
                $"take must be at most {maxPageSize}, was {parsed.ToString(CultureInfo.InvariantCulture)}. "
                + $"Ask for '{HttpApiConstants.AllItems}' to take as many as the server allows, page the request, "
                + $"or raise {MaxPageSizeOption}.");
        }

        return parsed;
    }

    /// <summary>
    /// The option a caller refused for its page size is told to raise, named as the HTTP API spells it.
    /// </summary>
    private const string MaxPageSizeOption = "QuartzHttpApiOptions.MaxPageSize";

    private static FireInstanceState? ReadState(string? state)
    {
        if (state is null || string.Equals(state, HttpApiConstants.AnyFireInstanceState, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Enum.TryParse(state, ignoreCase: true, out FireInstanceState value) && Enum.IsDefined(value))
        {
            return value;
        }

        throw new InvalidRequestException($"Unknown fire instance state '{state}'");
    }

    private static void RequireOneRule(string? contains, string? endsWith, string? startsWith, string? equals)
    {
        int given = 0;
        foreach (string? value in (ReadOnlySpan<string?>) [contains, endsWith, startsWith, equals])
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                given++;
            }
        }

        if (given > 1)
        {
            throw new InvalidRequestException("Only single match rule can be given");
        }
    }
}

/// <summary>
/// A request the wire contract refuses as malformed, before or instead of anything reaching a scheduler.
/// </summary>
/// <remarks>
/// The catalogue's counterpart of ASP.NET Core's <c>BadHttpRequestException</c>, which it cannot raise:
/// a <c>400</c> that says what to fix. The HTTP API answers it exactly as it answers that one, naming it
/// <see cref="HttpApiConstants.RequestRefusedExceptionType" /> on the wire.
/// </remarks>
internal sealed class InvalidRequestException : Exception
{
    public InvalidRequestException(string message) : base(message)
    {
    }

    public InvalidRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
