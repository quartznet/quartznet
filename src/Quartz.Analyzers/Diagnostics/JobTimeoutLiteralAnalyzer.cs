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

using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quartz.Analyzers;

/// <summary>
/// Reads the <c>[JobTimeout("…")]</c> argument the way the attribute's constructor does.
/// </summary>
/// <remarks>
/// Nothing runs that constructor until the timeout middleware reflects over the job type, which is
/// the first firing at the earliest and a deployment away at worst. The two messages below are the
/// attribute's own, so the compiler and the run time say the same thing about the same string.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JobTimeoutLiteralAnalyzer : DiagnosticAnalyzer
{
    private const string JobTimeoutAttributeTypeName = "Quartz.JobTimeoutAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InvalidJobTimeout);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax) context.Node;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor
            || constructor.ContainingType.ToDisplayString() != JobTimeoutAttributeTypeName)
        {
            return;
        }

        AttributeArgumentSyntax? argument = FindTimeoutArgument(attribute);
        if (argument is null)
        {
            return;
        }

        Optional<object?> constant = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string timeout)
        {
            return;
        }

        string? message = Validate(timeout);
        if (message is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidJobTimeout, argument.Expression.GetLocation(), message));
        }
    }

    /// <summary>
    /// What <c>JobTimeoutAttribute</c>'s constructor would have thrown, or <see langword="null" />
    /// when the string is a budget.
    /// </summary>
    private static string? Validate(string timeout)
    {
        if (!TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return $"'{timeout}' is not a TimeSpan. Spell the job's timeout the way TimeSpan does, invariantly: \"00:05:00\" for five minutes, \"1.00:00:00\" for a day.";
        }

        if (parsed < TimeSpan.Zero)
        {
            return $"A job's timeout cannot be negative, and '{timeout}' is. Use \"00:00:00\" to say the job has no timeout.";
        }

        return null;
    }

    /// <summary>
    /// The attribute's single positional argument. A named one is a property initialiser, and
    /// <c>JobTimeoutAttribute</c> has no settable property to be one of.
    /// </summary>
    private static AttributeArgumentSyntax? FindTimeoutArgument(AttributeSyntax attribute)
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
}
