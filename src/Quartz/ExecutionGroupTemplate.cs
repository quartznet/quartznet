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
using System.Text;

namespace Quartz;

/// <summary>
/// The <c>{key}</c> placeholders an execution group may carry, such as <c>tenant:{TenantId}</c>, and the
/// one grammar every place that resolves them shares.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is a composite format string's, without alignment or format: <c>{key}</c> is replaced by
/// the value under <c>key</c>, <c>{{</c> is a literal <c>{</c> and <c>}}</c> a literal <c>}</c>. An
/// unclosed <c>{</c>, a lone <c>}</c> and an empty <c>{}</c> are refused, so a name with a brace in it has
/// one spelling rather than a guess.
/// </para>
/// <para>
/// A template is resolved once, when its trigger is built, and the trigger stores the result: nothing on
/// the wire or in the store knows a template existed. <see cref="Escape" /> is how a stored name goes back
/// into a builder unchanged.
/// </para>
/// </remarks>
internal static class ExecutionGroupTemplate
{
    /// <summary>
    /// Where a template reads its values: whether <paramref name="key" /> is there, and what it holds.
    /// </summary>
    internal delegate bool ValueLookup(string key, out object? value);

    /// <summary>
    /// Whether the text has anything to resolve or unescape. A name with no brace is its own value.
    /// </summary>
    internal static bool IsTemplate(string text)
    {
        return text.AsSpan().IndexOfAny('{', '}') >= 0;
    }

    /// <summary>
    /// Whether a template <see cref="Validate" /> accepts names any key, rather than only escaping braces.
    /// </summary>
    internal static bool HasPlaceholders(string template)
    {
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                continue;
            }

            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                i++;
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Refuses a template the grammar cannot read, before anything is built from it.
    /// </summary>
    /// <exception cref="ArgumentException">The template is malformed; the message says where.</exception>
    internal static void Validate(string template, string parameterName)
    {
        if (Scan(template, lookup: null, source: null, resolved: null) is { } error)
        {
            Throw.ArgumentException(error, parameterName);
        }
    }

    /// <summary>
    /// Replaces each placeholder with the value <paramref name="lookup" /> holds for it.
    /// </summary>
    /// <param name="template">A template <see cref="Validate" /> accepts.</param>
    /// <param name="lookup">Where the values come from.</param>
    /// <param name="source">What <paramref name="lookup" /> reads, for the message when a key is not there.</param>
    /// <returns>The execution group, trimmed, which is what a trigger stores.</returns>
    /// <exception cref="FormatException">The template names a key that is missing or null, is malformed,
    /// or resolves to a blank or reserved name.</exception>
    internal static string Resolve(string template, ValueLookup lookup, string source)
    {
        StringBuilder resolved = new(template.Length + 16);
        if (Scan(template, lookup, source, resolved) is { } error)
        {
            throw new FormatException(error);
        }

        string group = resolved.ToString().Trim();

        if (group.Length == 0)
        {
            throw new FormatException($"The execution group template '{template}' resolves to an empty name.");
        }

        if (ExecutionLimits.IsReservedGroupName(group))
        {
            throw new FormatException($"The execution group template '{template}' resolves to '{group}', which is reserved for limits configuration.");
        }

        return group;
    }

    /// <summary>
    /// The template that resolves to <paramref name="literal" /> itself: every brace doubled.
    /// </summary>
    internal static string Escape(string literal)
    {
        return IsTemplate(literal)
            ? literal.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)
            : literal;
    }

    /// <summary>
    /// Walks the template once, and returns what is wrong with it, or <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// With a <paramref name="lookup" /> the walk also fills <paramref name="resolved" />; without one it
    /// only checks the grammar, which is how <see cref="Validate" /> and <see cref="Resolve" /> cannot
    /// disagree about what a template is.
    /// </remarks>
    private static string? Scan(string template, ValueLookup? lookup, string? source, StringBuilder? resolved)
    {
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    resolved?.Append('{');
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    return $"The execution group template '{template}' opens a placeholder at position {i} and never closes it. Write '{{{{' for a literal '{{'.";
                }

                string key = template.Substring(i + 1, close - i - 1);
                if (key.Length == 0)
                {
                    return $"The execution group template '{template}' has an empty placeholder '{{}}' at position {i}.";
                }

                if (key.Contains('{', StringComparison.Ordinal))
                {
                    return $"The execution group template '{template}' opens a placeholder inside another at position {i}. Write '{{{{' for a literal '{{'.";
                }

                if (lookup is not null)
                {
                    if (!lookup(key, out object? value))
                    {
                        return $"The execution group template '{template}' names '{key}', which is not in {source}.";
                    }

                    if (value is null)
                    {
                        return $"The execution group template '{template}' names '{key}', which is null in {source}.";
                    }

                    resolved!.Append(Format(value));
                }

                i = close + 1;
                continue;
            }

            if (c == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    resolved?.Append('}');
                    i += 2;
                    continue;
                }

                return $"The execution group template '{template}' has a '}}' at position {i} that closes nothing. Write '}}}}' for a literal '}}'.";
            }

            resolved?.Append(c);
            i++;
        }

        return null;
    }

    /// <summary>
    /// A value as a group name spells it: invariantly, so the same tenant is the same group on every
    /// node whatever its culture.
    /// </summary>
    private static string Format(object value)
    {
        return value switch
        {
            string text => text,
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
