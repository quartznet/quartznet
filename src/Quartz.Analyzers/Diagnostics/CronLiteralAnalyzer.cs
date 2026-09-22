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

using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Quartz.Analyzers;

/// <summary>
/// Reads every cron expression written as a literal or a constant, at the calls that take one.
/// </summary>
/// <remarks>
/// <para>
/// A call is recognised by its symbol - the type it is declared on and the name of the parameter that
/// carries the expression - never by the text at the call site, so a <c>using static</c>, an alias or
/// a differently named local makes no difference and a method of somebody else's called
/// <c>WithCronSchedule</c> is not one of these.
/// </para>
/// <para>
/// Anything whose value the compiler does not know is skipped in silence: a variable, a field read, a
/// string built at run time, an interpolation with a hole in it. A literal, a <c>const</c> and an
/// interpolated string with no holes are the same thing to the semantic model, which is exactly the
/// set this is meant to read.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CronLiteralAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The type of the sibling argument that says which dialect the expression is written in.
    /// </summary>
    private const string CronFormatTypeName = "Quartz.CronFormat";

    /// <summary>
    /// Every entry point that reads a cron expression string, and what it does with it.
    /// </summary>
    /// <remarks>
    /// Keyed by the declaring type and the member name, because that pair is what the symbol gives
    /// and what a rename would move. <c>H</c>-resolving entry points accept expressions the others
    /// reject, so the flag is part of the identity of the call rather than a property of cron.
    /// </remarks>
    private static readonly Dictionary<string, CronEntryPoint> EntryPoints = new Dictionary<string, CronEntryPoint>(StringComparer.Ordinal)
    {
        ["Quartz.CronScheduleBuilder.Create"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        ["Quartz.TriggerConfiguratorExtensions.WithCronSchedule"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        ["Quartz.CronExpression..ctor"] = new CronEntryPoint("cronExpression", resolvesHash: false),
        ["Quartz.CronExpression.Parse"] = new CronEntryPoint("cronExpression", resolvesHash: false),
        ["Quartz.CronExpression.TryParse"] = new CronEntryPoint("cronExpression", resolvesHash: false),
        ["Quartz.CronExpression.ParseWithHash"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        ["Quartz.CronExpression.TryParseWithHash"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        ["Quartz.CronExpression.ResolveHash"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        // The attribute a job declares its schedule with. It resolves H, because what the generator
        // emits for it is WithCronSchedule, which resolves H against the trigger's key.
        ["Quartz.CronTriggerAttribute..ctor"] = new CronEntryPoint("cronExpression", resolvesHash: true),
        ["Quartz.Impl.Calendar.CronCalendar..ctor"] = new CronEntryPoint("expression", resolvesHash: false),
        ["Quartz.Impl.Triggers.CronTriggerImpl..ctor"] = new CronEntryPoint("cronExpression", resolvesHash: false),
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.InvalidCronExpression);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation invocation = (IInvocationOperation) context.Operation;
        Analyze(context, invocation.TargetMethod, invocation.Arguments);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation creation = (IObjectCreationOperation) context.Operation;
        if (creation.Constructor is not null)
        {
            Analyze(context, creation.Constructor, creation.Arguments);
        }
    }

    private static void Analyze(OperationAnalysisContext context, IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments)
    {
        string key = method.ContainingType.ToDisplayString() + "." + method.Name;
        if (!EntryPoints.TryGetValue(key, out CronEntryPoint entryPoint))
        {
            return;
        }

        IArgumentOperation? expressionArgument = Find(arguments, x =>
            x.Parameter?.Name == entryPoint.ExpressionParameterName
            && x.Parameter.Type.SpecialType == SpecialType.System_String);

        if (expressionArgument?.Value.ConstantValue is not { HasValue: true } constant)
        {
            return;
        }

        if (constant.Value is not string expression || string.IsNullOrWhiteSpace(expression))
        {
            ReportMissing(context, expressionArgument, constant.Value as string);
            return;
        }

        if (!TryReadFormat(arguments, out CronFormat format))
        {
            // The dialect is decided at run time, and the same five fields mean different days in
            // each of them, so there is nothing to check rather than something to guess.
            return;
        }

        string? message = CronLiteralValidator.Validate(expression, format, entryPoint.ResolvesHash);
        if (message is not null)
        {
            Report(context, expressionArgument, "'" + expression + "' is not a valid cron expression: " + message);
        }
    }

    /// <summary>
    /// A constant expression that is null, empty or only whitespace, which no parser reads as a
    /// schedule — so it is reported as missing rather than as a parse error about zero fields.
    /// </summary>
    /// <remarks>
    /// A Try method's <c>string?</c> takes a null and answers <see langword="false" />, which is its
    /// contract. Everywhere else the parameter is not nullable and the call throws for a null the
    /// moment it runs; <c>[CronTrigger(null!)]</c> did so while the host was starting.
    /// </remarks>
    private static void ReportMissing(OperationAnalysisContext context, IArgumentOperation expressionArgument, string? expression)
    {
        if (expression is null && expressionArgument.Parameter!.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return;
        }

        Report(context, expressionArgument, "The cron expression is missing: the argument is " + Describe(expression));
    }

    private static void Report(OperationAnalysisContext context, IArgumentOperation expressionArgument, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.InvalidCronExpression,
            expressionArgument.Value.Syntax.GetLocation(),
            message));
    }

    /// <summary>
    /// What a missing expression was written as. Whitespace is missing too: the parser trims before it
    /// reads, so it sees exactly what an empty string gives it.
    /// </summary>
    private static string Describe(string? expression) => expression switch
    {
        null => "null",
        "" => "an empty string",
        _ => "only whitespace",
    };

    /// <summary>
    /// The dialect the sibling <see cref="CronFormat" /> argument names, or
    /// <see cref="CronFormat.Quartz" /> when the call has no such argument.
    /// </summary>
    /// <returns>
    /// <see langword="false" /> when there is a format argument whose value the compiler does not
    /// know, which is the one case where the expression cannot be read at all.
    /// </returns>
    private static bool TryReadFormat(ImmutableArray<IArgumentOperation> arguments, out CronFormat format)
    {
        format = CronFormat.Quartz;

        IArgumentOperation? formatArgument = Find(arguments, x =>
            x.Parameter?.Type.ToDisplayString() == CronFormatTypeName);

        if (formatArgument is null)
        {
            return true;
        }

        if (formatArgument.Value.ConstantValue is not { HasValue: true, Value: int value })
        {
            return false;
        }

        format = (CronFormat) value;
        return true;
    }

    private static IArgumentOperation? Find(ImmutableArray<IArgumentOperation> arguments, Func<IArgumentOperation, bool> predicate)
    {
        foreach (IArgumentOperation argument in arguments)
        {
            if (predicate(argument))
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>
    /// One call that reads a cron expression string.
    /// </summary>
    private readonly struct CronEntryPoint
    {
        internal CronEntryPoint(string expressionParameterName, bool resolvesHash)
        {
            ExpressionParameterName = expressionParameterName;
            ResolvesHash = resolvesHash;
        }

        internal string ExpressionParameterName { get; }

        internal bool ResolvesHash { get; }
    }
}
