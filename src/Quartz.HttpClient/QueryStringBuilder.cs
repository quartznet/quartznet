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
using Quartz.Util;

namespace Quartz;

internal sealed class QueryStringBuilder
{
    private readonly List<string> parameters = [];

    public void Add(string name, string value) => parameters.Add($"{name}={Uri.EscapeDataString(value)}");

    public void Add(string name, int value) => parameters.Add($"{name}={value.ToString(CultureInfo.InvariantCulture)}");

    public void Add(string name, bool value) => parameters.Add($"{name}={(value ? "true" : "false")}");

    public void AddPaging(PagedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Skip != 0)
        {
            Add("skip", query.Skip);
        }

        // Always sent: omitting the parameter would hand the decision to the server's default,
        // and Take = int.MaxValue - the explicit unbounded opt-in - would silently truncate.
        //
        // Spelled the way the API spells it. A server with QuartzHttpApiOptions.MaxPageSize set refuses
        // a number above the cap and bounds "all" to it, so a listing asking for everything answers
        // whatever the server will give and says hasMore; the number would be a 400 however few rows
        // match. HttpScheduler is the one that reads hasMore back.
        if (query.Take == PagedQuery.All)
        {
            Add("take", HttpApiConstants.AllItems);
        }
        else
        {
            Add("take", query.Take);
        }

        if (query.IncludeTotalCount)
        {
            Add("includeTotalCount", value: true);
        }
    }

    public void AddGroupMatcher<T>(GroupMatcher<T>? matcher) where T : Key<T>
    {
        if (matcher is null)
        {
            return;
        }

        string urlParameters = matcher.ToUrlParameters();
        if (urlParameters.Length > 0)
        {
            parameters.Add(urlParameters);
        }
    }

    public void AddNameMatcher<T>(NameMatcher<T>? matcher) where T : Key<T>
    {
        if (matcher is null)
        {
            return;
        }

        string urlParameters = matcher.ToUrlParameters();
        if (urlParameters.Length > 0)
        {
            parameters.Add(urlParameters);
        }
    }

    public void AddNameMatcher(NameMatcher? matcher)
    {
        if (matcher is null)
        {
            return;
        }

        string urlParameters = matcher.ToUrlParameters();
        if (urlParameters.Length > 0)
        {
            parameters.Add(urlParameters);
        }
    }

    public override string ToString() => parameters.Count == 0 ? "" : "?" + string.Join('&', parameters);
}
