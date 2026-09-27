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

using System.Diagnostics.CodeAnalysis;

namespace Quartz.HttpApiContract;

/// <summary>
/// One route of the wire contract: the operation's name, its method, and its path template relative to
/// wherever the API is mapped.
/// </summary>
/// <remarks>
/// <para>
/// The same object is read by both ends. The server maps its endpoint at <see cref="Template" /> under
/// the name <see cref="Name" />, and a client fills the template in with <see cref="For" /> and sends
/// <see cref="Method" />, so the two cannot spell a path differently. <see cref="SchedulerRoutes" /> holds
/// every one of them.
/// </para>
/// <para>
/// A template is literal segments and <c>{name}</c> parameters separated by <c>/</c>, which is the subset
/// of ASP.NET Core's route syntax the API uses: no constraints, no defaults, no catch-alls.
/// </para>
/// </remarks>
internal sealed class WireRoute
{
    private readonly string[] segments;
    private readonly bool[] isParameter;

    public WireRoute(string name, string method, string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        Name = name;
        Method = method;
        Template = template;

        segments = template.Split('/');
        isParameter = new bool[segments.Length];

        List<string> parameters = [];
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}')
            {
                isParameter[i] = true;
                segments[i] = segment[1..^1];
                parameters.Add(segments[i]);
            }
        }

        Parameters = parameters.AsReadOnly();
    }

    /// <summary>
    /// The operation's name, which is the endpoint's name on the server — its OpenAPI operation id and
    /// the word the mutation audit logs.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The HTTP method, upper case.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// The path template, relative to the API's root: <c>schedulers/{schedulerName}/jobs</c>.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// The template's parameters, in the order <see cref="For" /> takes their values.
    /// </summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// A request to this route with the template's parameters filled in, in order.
    /// </summary>
    /// <remarks>
    /// The values go into the path exactly as given, so escaping one is the caller's decision.
    /// <c>HttpScheduler</c> escapes the values it always escaped and no others, which keeps every path it
    /// sends what it was.
    /// </remarks>
    /// <exception cref="ArgumentException">The number of values is not the number of parameters.</exception>
    public WireRequest For(params ReadOnlySpan<string> values)
    {
        if (values.Length != Parameters.Count)
        {
            throw new ArgumentException(
                $"Route {Name} takes {Parameters.Count} values ({string.Join(", ", Parameters)}), was given {values.Length}.",
                nameof(values));
        }

        string[] filled = new string[segments.Length];
        int next = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            filled[i] = isParameter[i] ? values[next++] : segments[i];
        }

        return new WireRequest(this, string.Join('/', filled));
    }

    /// <summary>
    /// Whether <paramref name="path" /> is a path of this route, reading the parameters' values out of
    /// it when it is.
    /// </summary>
    /// <remarks>
    /// The rules ASP.NET Core's routing applies to templates of this shape: literals compare ignoring
    /// case, a parameter takes one non-empty segment, the values are unescaped, and neither a query
    /// string nor a leading or trailing slash takes part.
    /// </remarks>
    public bool TryMatch(string path, [NotNullWhen(true)] out Dictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(path);

        values = null;

        int query = path.IndexOf('?', StringComparison.Ordinal);
        ReadOnlySpan<char> remaining = (query >= 0 ? path.AsSpan(0, query) : path.AsSpan()).Trim('/');

        Dictionary<string, string> matched = new(StringComparer.Ordinal);
        for (int i = 0; i < segments.Length; i++)
        {
            if (remaining.IsEmpty)
            {
                return false;
            }

            int separator = remaining.IndexOf('/');
            ReadOnlySpan<char> segment = separator >= 0 ? remaining[..separator] : remaining;
            remaining = separator >= 0 ? remaining[(separator + 1)..] : [];

            if (segment.IsEmpty)
            {
                return false;
            }

            if (isParameter[i])
            {
                matched[segments[i]] = Uri.UnescapeDataString(segment.ToString());
            }
            else if (!segment.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!remaining.IsEmpty)
        {
            return false;
        }

        values = matched;
        return true;
    }

    public override string ToString() => $"{Method} {Template}";
}
