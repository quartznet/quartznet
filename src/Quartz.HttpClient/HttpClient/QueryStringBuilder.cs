using System.Globalization;

using Quartz.Impl.Matchers;
using Quartz.Util;

namespace Quartz.HttpClient;

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

        if (query.Take != int.MaxValue)
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

    public override string ToString() => parameters.Count == 0 ? "" : "?" + string.Join('&', parameters);
}
