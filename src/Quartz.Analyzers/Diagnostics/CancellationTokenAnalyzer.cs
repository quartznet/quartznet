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
/// Finds a job body that awaits or loops and never reads the token that would stop it.
/// </summary>
/// <remarks>
/// <para>
/// CA2016 catches a token that is not forwarded to a call that takes one; it says nothing about a job
/// that calls nothing token-aware and simply runs. This is that gap, and it is a heuristic, which is
/// why it is <see cref="DiagnosticSeverity.Info" />: whether a piece of work is interruptible is a
/// judgement, and a job that returns in a millisecond is right to ignore the token.
/// </para>
/// <para>
/// The parameter and <c>IJobExecutionContext.CancellationToken</c> are the same token, so reading
/// either counts. The <c>await</c>-or-loop condition is what keeps the rule off the jobs that could
/// not honour a cancellation anyway.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationTokenAnalyzer : DiagnosticAnalyzer
{
    private const string JobInterfaceTypeName = "Quartz.IJob";

    private const string GenericJobInterfaceTypeName = "Quartz.IJob`1";

    private const string JobExecutionContextTypeName = "Quartz.IJobExecutionContext";

    private const string CancellationTokenPropertyName = "CancellationToken";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.CancellationTokenNotObserved);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            INamedTypeSymbol? job = compilationStart.Compilation.GetTypeByMetadataName(JobInterfaceTypeName);
            INamedTypeSymbol? genericJob = compilationStart.Compilation.GetTypeByMetadataName(GenericJobInterfaceTypeName);
            INamedTypeSymbol? executionContext = compilationStart.Compilation.GetTypeByMetadataName(JobExecutionContextTypeName);

            if (job is null || executionContext is null)
            {
                return;
            }

            compilationStart.RegisterSyntaxNodeAction(
                syntaxContext => Analyze(syntaxContext, job, genericJob, executionContext),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol job,
        INamedTypeSymbol? genericJob,
        INamedTypeSymbol executionContext)
    {
        MethodDeclarationSyntax declaration = (MethodDeclarationSyntax) context.Node;

        if (declaration.Body is null && declaration.ExpressionBody is null)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method
            || !IsJobExecution(method, job, genericJob))
        {
            return;
        }

        IParameterSymbol? token = FindCancellationTokenParameter(method);
        if (token is null)
        {
            return;
        }

        SyntaxNode body = (SyntaxNode?) declaration.Body ?? declaration.ExpressionBody!;

        if (!AwaitsOrLoops(body) || ReadsToken(context.SemanticModel, body, token, executionContext, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors.CancellationTokenNotObserved,
            declaration.Identifier.GetLocation(),
            method.ContainingType.Name + "." + method.Name));
    }

    /// <summary>
    /// Whether this method is what <c>IJob.Execute</c> resolves to on its type - the implementation,
    /// explicit or not, or an override of it - rather than anything that happens to be called
    /// <c>Execute</c>.
    /// </summary>
    private static bool IsJobExecution(IMethodSymbol method, INamedTypeSymbol job, INamedTypeSymbol? genericJob)
    {
        INamedTypeSymbol type = method.ContainingType;

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            INamedTypeSymbol definition = implemented.OriginalDefinition;

            if (!SymbolEqualityComparer.Default.Equals(definition, job)
                && (genericJob is null || !SymbolEqualityComparer.Default.Equals(definition, genericJob)))
            {
                continue;
            }

            foreach (ISymbol member in implemented.GetMembers("Execute"))
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation
                    && IsOrOverrides(method, implementation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the method is the interface's implementation, or overrides it however many classes down.
    /// </summary>
    /// <remarks>
    /// The implementation is the member that maps to the interface, which for a virtual or abstract
    /// <c>Execute</c> on a base job is the base's - while the body the scheduler runs is the override.
    /// A partial method maps through its declaration, and its body is on the other part. A <c>new</c>
    /// method overrides nothing, so a derived job hiding its base's <c>Execute</c> is still not the
    /// body the scheduler runs, and is not read.
    /// </remarks>
    private static bool IsOrOverrides(IMethodSymbol method, IMethodSymbol implementation)
    {
        for (IMethodSymbol? current = method.PartialDefinitionPart ?? method; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current, implementation))
            {
                return true;
            }
        }

        return false;
    }

    private static IParameterSymbol? FindCancellationTokenParameter(IMethodSymbol method)
    {
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                return parameter;
            }
        }

        return null;
    }

    private static bool AwaitsOrLoops(SyntaxNode body)
    {
        foreach (SyntaxNode node in body.DescendantNodes())
        {
            if (node is AwaitExpressionSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or ForEachVariableStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether anything in the body names the token parameter, or reads
    /// <c>IJobExecutionContext.CancellationToken</c>, which is the same token.
    /// </summary>
    private static bool ReadsToken(
        SemanticModel model,
        SyntaxNode body,
        IParameterSymbol token,
        INamedTypeSymbol executionContext,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxNode node in body.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax identifier)
            {
                continue;
            }

            ISymbol? symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;

            if (SymbolEqualityComparer.Default.Equals(symbol, token))
            {
                return true;
            }

            if (symbol is IPropertySymbol property
                && property.Name == CancellationTokenPropertyName
                && SymbolEqualityComparer.Default.Equals(property.ContainingType, executionContext))
            {
                return true;
            }
        }

        return false;
    }
}
