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
/// Reads each delay a <c>[RetryPolicy(…)]</c> is written with the way the attribute's constructor does.
/// </summary>
/// <remarks>
/// Every string the attribute takes is a delay: the fixed and exponential forms' second argument, each
/// argument of the explicit form, and <c>MaxDelay</c>. The scheduler reads the attribute when the job is
/// added, which is start-up at the earliest. The messages are the attribute's own, so the compiler and the
/// run time say the same thing about the same string. The counts, the factor and the jitter are not read
/// here.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RetryPolicyLiteralAnalyzer : DiagnosticAnalyzer
{
    private const string RetryPolicyAttributeTypeName = "Quartz.RetryPolicyAttribute";
    private const string MaxDelayProperty = "MaxDelay";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InvalidRetryPolicyDelay);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax) context.Node;

        if (attribute.ArgumentList is null
            || context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor
            || constructor.ContainingType.ToDisplayString() != RetryPolicyAttributeTypeName)
        {
            return;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            // A constructor argument, or the one named property that is a delay. Jitter is a double.
            if (argument.NameEquals is not null && argument.NameEquals.Name.Identifier.ValueText != MaxDelayProperty)
            {
                continue;
            }

            Optional<object?> constant = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
            if (!constant.HasValue || constant.Value is not string delay)
            {
                continue;
            }

            string? message = Validate(delay);
            if (message is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidRetryPolicyDelay, argument.Expression.GetLocation(), message));
            }
        }
    }

    /// <summary>
    /// What <c>RetryPolicyAttribute</c> would have thrown for the delay, or <see langword="null" /> when
    /// it is one.
    /// </summary>
    private static string? Validate(string delay)
    {
        if (!AttributeLiterals.TryParseTimeSpan(delay, out TimeSpan parsed))
        {
            return $"'{delay}' is not a TimeSpan. Spell a retry delay the way TimeSpan does, invariantly: \"00:00:30\" for thirty seconds, \"1.00:00:00\" for a day.";
        }

        if (parsed < TimeSpan.Zero)
        {
            return "A retry delay of " + parsed.ToString("c", CultureInfo.InvariantCulture) + " is negative; a retry cannot be scheduled into the past.";
        }

        return null;
    }
}
