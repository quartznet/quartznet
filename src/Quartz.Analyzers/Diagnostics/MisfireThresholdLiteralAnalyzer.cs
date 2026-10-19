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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quartz.Analyzers;

/// <summary>
/// Reads the <c>MisfireThreshold = "…"</c> of a <c>[CronTrigger]</c> or <c>[SimpleTrigger]</c> the way
/// the attributes' property and the generator do.
/// </summary>
/// <remarks>
/// <para>
/// The generator carries the threshold into the registration as ticks, so nothing parses it at run time
/// and a value it cannot read has to be refused here. The messages are the attributes' own
/// (<c>Quartz.Util.MisfireThresholdLiteral</c>), so the compiler and a reflection over the attribute say
/// the same thing about the same string.
/// </para>
/// <para>
/// <see cref="Validate" /> is what the generator asks too, so it leaves out exactly the thresholds this
/// reports.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MisfireThresholdLiteralAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The named property the threshold is written as, on both attributes.
    /// </summary>
    internal const string MisfireThresholdPropertyName = "MisfireThreshold";

    private const string CronTriggerAttributeTypeName = "Quartz.CronTriggerAttribute";

    private const string SimpleTriggerAttributeTypeName = "Quartz.SimpleTriggerAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InvalidMisfireThreshold);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    /// <summary>
    /// What the attributes' <c>MisfireThreshold</c> would have thrown, or <see langword="null" /> when the
    /// string is a threshold a trigger can have.
    /// </summary>
    internal static string? Validate(string threshold, out TimeSpan parsed)
    {
        if (!AttributeLiterals.TryParseTimeSpan(threshold, out parsed))
        {
            return $"'{threshold}' is not a TimeSpan. Spell the trigger's misfire threshold the way TimeSpan does, invariantly: \"00:05:00\" for five minutes.";
        }

        if (parsed <= TimeSpan.Zero)
        {
            return $"A trigger's misfire threshold has to be longer than zero, and '{threshold}' is not.";
        }

        if (parsed.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            return $"A trigger's misfire threshold is a whole number of milliseconds, and '{threshold}' is not.";
        }

        return null;
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax) context.Node;

        AttributeArgumentSyntax? argument = AttributeLiterals.FindNamedArgument(attribute, MisfireThresholdPropertyName);
        if (argument is null)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor)
        {
            return;
        }

        string attributeType = constructor.ContainingType.ToDisplayString();
        if (attributeType != CronTriggerAttributeTypeName && attributeType != SimpleTriggerAttributeTypeName)
        {
            return;
        }

        Optional<object?> constant = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string threshold)
        {
            return;
        }

        string? message = Validate(threshold, out _);
        if (message is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidMisfireThreshold, argument.Expression.GetLocation(), message));
        }
    }
}
