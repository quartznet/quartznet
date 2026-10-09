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

    public WireRoute(string name, string method, string template, bool mutates = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        Name = name;
        Method = method;
        Template = template;
        Mutates = mutates;

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
    /// Whether a call of this route changes something, which is what a read-only carrier refuses.
    /// </summary>
    /// <remarks>
    /// A property of the route rather than of its verb: the three bulk fetches are <c>POST</c>s that take
    /// a body of keys and change nothing, so they are served read-only, and everything else that is not a
    /// <c>GET</c> is refused. The HTTP API marks its endpoints the same way, and <c>ReadOnlyApiTest</c>
    /// holds the two markings to each other. Added in 4.5.
    /// </remarks>
    public bool Mutates { get; }

    /// <summary>
    /// A request to this route with the template's parameters filled in, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each value is escaped with <see cref="Uri.EscapeDataString(string)" />, so a <c>?</c>, <c>#</c>,
    /// <c>%</c>, <c>&amp;</c> or space in a name reaches the server as written: ASP.NET Core unescapes the
    /// path before it routes. A value made only of letters, digits and <c>-._~</c> goes in unchanged.
    /// </para>
    /// <para>
    /// A value no escaping brings back is refused. ASP.NET Core keeps an escaped <c>/</c> escaped and routing
    /// does not unescape it, so <c>a/b</c> would arrive as <c>a%2Fb</c>, which is also what a name spelled
    /// <c>a%2Fb</c> arrives as. A value of <c>.</c> or <c>..</c> is a dot segment, removed from the path
    /// before routing, so the request would reach another route. Either would be answered for the wrong
    /// name without a word.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The number of values is not the number of parameters, or a value contains <c>/</c> or is <c>.</c>
    /// or <c>..</c>.
    /// </exception>
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
            filled[i] = isParameter[i] ? Escape(Parameters[next], values[next++]) : segments[i];
        }

        return new WireRequest(this, string.Join('/', filled));
    }

    /// <summary>
    /// A value as it goes into a path segment, or a refusal when the server could not read it back.
    /// </summary>
    private string Escape(string parameter, string value)
    {
        if (value.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The {parameter} '{value}' cannot be sent in the path of {Name}: it contains '/', which ASP.NET Core "
                + "keeps escaped and routing does not unescape, so the server would read a different name. Rename it "
                + "without '/', or use a member that takes a set of keys, which sends them in the body.",
                parameter);
        }

        if (value is "." or "..")
        {
            throw new ArgumentException(
                $"The {parameter} '{value}' cannot be sent in the path of {Name}: a path segment of '{value}' is removed "
                + "before routing, so the request would reach another route. Rename it, or use a member that takes a "
                + "set of keys, which sends them in the body.",
                parameter);
        }

        return Uri.EscapeDataString(value);
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
