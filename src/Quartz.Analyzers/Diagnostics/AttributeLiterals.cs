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

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quartz.Analyzers;

/// <summary>
/// How an attribute that takes a <see cref="TimeSpan" /> as a string is read at build time: the
/// argument it was written as, and the parse its constructor runs.
/// </summary>
/// <remarks>
/// One place, so that <c>[JobTimeout]</c>'s analyzer, <c>[SimpleTrigger]</c>'s analyzer and the
/// generator that turns <c>[SimpleTrigger]</c> into ticks cannot come to read the same string two ways.
/// </remarks>
internal static class AttributeLiterals
{
    /// <summary>
    /// Parses the string as the attributes' constructors do: <see cref="TimeSpan" />'s own format,
    /// invariantly, so the same source reads the same on every build machine.
    /// </summary>
    internal static bool TryParseTimeSpan(string text, out TimeSpan value)
        => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// The attribute's constructor argument, for an attribute whose constructor takes one. A
    /// <c>Name = value</c> argument is a property initialiser; a <c>name: value</c> one is still the
    /// constructor's.
    /// </summary>
    internal static AttributeArgumentSyntax? FindPositionalArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList is null)
        {
            return null;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            if (argument.NameEquals is null)
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>
    /// The initialiser of the named property <paramref name="name" />, as <c>Name = value</c> writes it.
    /// </summary>
    internal static AttributeArgumentSyntax? FindNamedArgument(AttributeSyntax attribute, string name)
    {
        if (attribute.ArgumentList is null)
        {
            return null;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == name)
            {
                return argument;
            }
        }

        return null;
    }
}
