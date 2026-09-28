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
/// Reads the <c>[SimpleTrigger("…", RepeatCount = …)]</c> arguments the way the attribute's constructor
/// and the generator do.
/// </summary>
/// <remarks>
/// <para>
/// The generator carries the interval into the registration as ticks, so nothing parses it at run
/// time and a value it cannot read has to be refused here. The messages are the attribute's own, so
/// the compiler and a reflection over the attribute say the same thing about the same value.
/// </para>
/// <para>
/// <see cref="ValidateInterval" /> and <see cref="ValidateRepeatCount" /> are what the generator asks
/// too, so it skips exactly the schedules this reports.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SimpleTriggerLiteralAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// <c>SimpleTriggerImpl.RepeatIndefinitely</c>: the repeat count that repeats forever, and the
    /// lowest one a trigger takes.
    /// </summary>
    internal const int RepeatIndefinitely = -1;

    /// <summary>
    /// The named property the repeat count is written as.
    /// </summary>
    internal const string RepeatCountPropertyName = "RepeatCount";

    private const string SimpleTriggerAttributeTypeName = "Quartz.SimpleTriggerAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InvalidSimpleTriggerSchedule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    /// <summary>
    /// What <c>SimpleTriggerAttribute</c>'s constructor would have thrown, or <see langword="null" />
    /// when the string is an interval a trigger can have.
    /// </summary>
    internal static string? ValidateInterval(string? interval, out TimeSpan parsed)
    {
        parsed = TimeSpan.Zero;

        if (interval is null)
        {
            return "The interval is missing: the argument is null.";
        }

        if (!AttributeLiterals.TryParseTimeSpan(interval, out parsed))
        {
            return $"'{interval}' is not a TimeSpan. Spell the trigger's interval the way TimeSpan does, invariantly: \"00:10:00\" for ten minutes, \"1.00:00:00\" for a day.";
        }

        if (parsed <= TimeSpan.Zero)
        {
            return $"A trigger's interval has to be longer than zero, and '{interval}' is not.";
        }

        return null;
    }

    /// <summary>
    /// What <c>SimpleTriggerAttribute.RepeatCount</c>'s setter would have thrown, or
    /// <see langword="null" /> for a count a trigger can have.
    /// </summary>
    internal static string? ValidateRepeatCount(int repeatCount)
    {
        return repeatCount < RepeatIndefinitely
            ? $"RepeatCount cannot be {repeatCount.ToString(CultureInfo.InvariantCulture)}: it counts the firings after the first, so it is 0 or more, or -1 to repeat forever."
            : null;
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax) context.Node;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor
            || constructor.ContainingType.ToDisplayString() != SimpleTriggerAttributeTypeName)
        {
            return;
        }

        AttributeArgumentSyntax? interval = AttributeLiterals.FindPositionalArgument(attribute);
        if (interval is not null)
        {
            Optional<object?> constant = context.SemanticModel.GetConstantValue(interval.Expression, context.CancellationToken);
            if (constant.HasValue && constant.Value is null or string)
            {
                Report(context, interval, ValidateInterval((string?) constant.Value, out _));
            }
        }

        AttributeArgumentSyntax? repeatCount = AttributeLiterals.FindNamedArgument(attribute, RepeatCountPropertyName);
        if (repeatCount is not null)
        {
            Optional<object?> constant = context.SemanticModel.GetConstantValue(repeatCount.Expression, context.CancellationToken);
            if (constant.HasValue && constant.Value is int value)
            {
                Report(context, repeatCount, ValidateRepeatCount(value));
            }
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, AttributeArgumentSyntax argument, string? message)
    {
        if (message is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.InvalidSimpleTriggerSchedule, argument.Expression.GetLocation(), message));
        }
    }
}
