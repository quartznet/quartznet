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
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Quartz.Analyzers;

/// <summary>
/// Binds each delegate job whose handler is a lambda or a method group when it is compiled, by
/// intercepting the <c>AddJob</c> or <c>ScheduleJob</c> call that adds it.
/// </summary>
/// <remarks>
/// <para>
/// The model is ASP.NET Core's request delegate generator. The call's handler has a delegate type the
/// compiler knows, so the generated code can resolve each parameter and call the handler directly:
/// the firing, its token, its scope, and <c>GetRequiredService</c> for the rest. The interceptor hands
/// the same call the handler together with that code, through
/// <c>QuartzBuilderExtensions.WithCompiledBinding</c>. Quartz still reads and refuses the handler as it
/// does without the generator, so the two paths differ in what a firing costs and in nothing else.
/// </para>
/// <para>
/// A call is left alone, and bound by reflection when it runs, whenever the generated code could not
/// say the same thing: a handler that is a <c>Delegate</c> variable rather than a lambda or a method
/// group; a delegate type the compiler made up, which a <c>ref</c> or optional parameter gets; a type
/// another file cannot name; and every shape Quartz refuses — a result, <c>async void</c>, a ref struct
/// — so that the refusal is Quartz's own. So is every call in a project below C# 11, whose compiler
/// cannot read the file-local types the interceptor is declared with, and every call in a project that
/// has not enabled <see cref="InterceptorsNamespace" />, where the compiler would refuse the interceptor.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class DelegateJobsGenerator : IIncrementalGenerator
{
    /// <summary>
    /// The namespace the interceptors are declared in, which a project has to list in
    /// <c>InterceptorsNamespaces</c>. <c>buildTransitive/net10.0/Quartz.targets</c> does it for every
    /// project referencing the package.
    /// </summary>
    internal const string InterceptorsNamespace = "Quartz.Generated";

    /// <summary>
    /// The compiler feature the <c>InterceptorsNamespaces</c> and <c>InterceptorsPreviewNamespaces</c>
    /// properties both arrive as: the <c>Csc</c> task joins the two into this one key, and it is the only
    /// one the compiler reads.
    /// </summary>
    private const string InterceptorsFeature = "InterceptorsNamespaces";

    private const string ExtensionsTypeName = "Quartz.QuartzBuilderExtensions";

    private const string HandlerParameterName = "handler";

    private const string JobConfigurator = "global::System.Action<global::Quartz.IJobConfigurator<global::Quartz.IJob>>";

    private const string JobConfiguratorWithServices = "global::System.Action<global::System.IServiceProvider, global::Quartz.IJobConfigurator<global::Quartz.IJob>>";

    private const string TriggerConfigurator = "global::System.Action<global::Quartz.ITriggerConfigurator<global::Quartz.IJob>>";

    private const string TriggerConfiguratorWithServices = "global::System.Action<global::System.IServiceProvider, global::Quartz.ITriggerConfigurator<global::Quartz.IJob>>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<bool> enabled = context.ParseOptionsProvider
            .Select(static (options, _) => CanIntercept(options));

        IncrementalValuesProvider<DelegateJobCall> calls = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (syntax, cancellationToken) => ReadCall(syntax, cancellationToken))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        context.RegisterSourceOutput(
            calls.Collect().Combine(enabled),
            static (production, source) => Execute(production, source.Left, source.Right));
    }

    /// <summary>
    /// Whether the compiler would accept an interceptor declared in <see cref="InterceptorsNamespace" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C# 11 is the floor because the interceptor and its attribute are file-local types, which is what
    /// keeps them from colliding with another generator's. Phase A's lambdas already need C# 10, which is
    /// when a lambda gained a delegate type of its own, so C# 10 is the one version that falls back.
    /// </para>
    /// <para>
    /// The namespace is matched exactly. The compiler reads the list more generously, and a namespace it
    /// would accept but this refuses costs a reflective binding rather than a build.
    /// </para>
    /// </remarks>
    internal static bool CanIntercept(ParseOptions options)
    {
        if (options is not CSharpParseOptions csharp || csharp.LanguageVersion < LanguageVersion.CSharp11)
        {
            return false;
        }

        if (!csharp.Features.TryGetValue(InterceptorsFeature, out string? namespaces))
        {
            return false;
        }

        foreach (string entry in namespaces.Split(';'))
        {
            if (string.Equals(entry.Trim(), InterceptorsNamespace, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A call named like one of the four members, which is all the syntax can say before the semantic
    /// model is asked.
    /// </summary>
    private static bool IsCandidate(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax { ArgumentList.Arguments.Count: >= 2 } invocation
            && NameOf(invocation.Expression) is { Identifier.ValueText: "AddJob" or "ScheduleJob" };
    }

    /// <summary>
    /// The simple name that denotes the called method, which is also the position an interceptor names.
    /// </summary>
    private static IdentifierNameSyntax? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name as IdentifierNameSyntax,
        MemberBindingExpressionSyntax memberBinding => memberBinding.Name as IdentifierNameSyntax,
        IdentifierNameSyntax identifier => identifier,
        _ => null,
    };

    private static DelegateJobCall? ReadCall(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax) context.Node;
        SemanticModel model = context.SemanticModel;

        if (model.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
        {
            return null;
        }

        IMethodSymbol method = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        if (MemberOf(method) is not { } member)
        {
            return null;
        }

        IArgumentOperation? handlerArgument = operation.Arguments
            .FirstOrDefault(static x => x.Parameter?.Name == HandlerParameterName);

        if (HandlerOf(handlerArgument?.Value) is not { } handler
            || handler.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, IsAnonymousType: false, DelegateInvokeMethod: { } invoke } delegateType)
        {
            return null;
        }

        Compilation compilation = model.Compilation;
        if (!IsNameable(delegateType, compilation) || CompletionOf(invoke.ReturnType, compilation) is not { } completion)
        {
            return null;
        }

        // A void handler is only refused when it is async void, which a lambda converted to Delegate
        // never is — its delegate type returns Task — and a method group is when its method says so.
        if (completion == HandlerCompletion.Void && IsAsync(handler.Target))
        {
            return null;
        }

        ImmutableArray<HandlerParameter>.Builder parameters = ImmutableArray.CreateBuilder<HandlerParameter>(invoke.Parameters.Length);
        foreach (IParameterSymbol parameter in invoke.Parameters)
        {
            // Refused by Quartz, which is the refusal the application should see: a location rather than a
            // value, and a type that cannot be boxed into the argument array the reflective path fills.
            if (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType)
            {
                return null;
            }

            parameters.Add(new HandlerParameter(SourceOf(parameter.Type, compilation), TypeName(parameter.Type)));
        }

        // Experimental in the Roslyn this assembly is built against, and the API the compiler's own
        // documentation tells a generator to use: the encoding it returns is the compiler's to define.
#pragma warning disable RSEXPERIMENTAL002
        InterceptableLocation? location = model.GetInterceptableLocation(invocation, cancellationToken);
#pragma warning restore RSEXPERIMENTAL002

        if (location is null || NameOf(invocation.Expression) is not { } name)
        {
            return null;
        }

        return new DelegateJobCall(
            member,
            TypeName(delegateType),
            new EquatableArray<HandlerParameter>(parameters.MoveToImmutable()),
            completion,
            new InterceptedCall(
                location.Version,
                location.Data,
                invocation.SyntaxTree.FilePath,
                name.SpanStart,
                DisplayLocation(name)));
    }

    /// <summary>
    /// Which of the four members a call binds to, or <see langword="null" /> for any other method —
    /// another <c>AddJob</c> overload, or one an application declared under the same name.
    /// </summary>
    private static DelegateJobMember? MemberOf(IMethodSymbol method)
    {
        if (method.IsGenericMethod
            || method.Parameters.Length != 4
            || method.ContainingType?.ToDisplayString() != ExtensionsTypeName
            || method.Parameters[2].Name != HandlerParameterName
            || method.Parameters[2].Type.SpecialType != SpecialType.System_Delegate)
        {
            return null;
        }

        string configurator = TypeName(method.Parameters[3].Type);

        return method.Name switch
        {
            "AddJob" when configurator == JobConfigurator => DelegateJobMember.AddJob,
            "AddJob" when configurator == JobConfiguratorWithServices => DelegateJobMember.AddJobWithServices,
            "ScheduleJob" when configurator == TriggerConfigurator => DelegateJobMember.ScheduleJob,
            "ScheduleJob" when configurator == TriggerConfiguratorWithServices => DelegateJobMember.ScheduleJobWithServices,
            _ => null,
        };
    }

    /// <summary>
    /// The delegate a lambda or a method group became on its way to the <c>Delegate</c> parameter, or
    /// <see langword="null" /> for a handler that was already a delegate.
    /// </summary>
    /// <remarks>
    /// Only the implicit conversion counts. It gives a lambda or a method group its own delegate type, so
    /// the delegate's parameters are the method's; an explicit <c>new Func&lt;…&gt;(Method)</c> or a cast
    /// may convert a method group with variance, which would make the generated code resolve a different
    /// type from the one Quartz reads off the method.
    /// </remarks>
    private static IDelegateCreationOperation? HandlerOf(IOperation? value)
    {
        if (value is not IConversionOperation { IsImplicit: true, Operand: IDelegateCreationOperation { IsImplicit: true } creation })
        {
            return null;
        }

        return creation.Target is IAnonymousFunctionOperation or IMethodReferenceOperation ? creation : null;
    }

    private static HandlerCompletion? CompletionOf(ITypeSymbol returnType, Compilation compilation)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return HandlerCompletion.Void;
        }

        if (SymbolEqualityComparer.Default.Equals(returnType, compilation.GetTypeByMetadataName("System.Threading.Tasks.Task")))
        {
            return HandlerCompletion.Task;
        }

        if (SymbolEqualityComparer.Default.Equals(returnType, compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask")))
        {
            return HandlerCompletion.ValueTask;
        }

        return null;
    }

    private static bool IsAsync(IOperation target) => target switch
    {
        IAnonymousFunctionOperation lambda => lambda.Symbol.IsAsync,
        IMethodReferenceOperation reference => reference.Method.IsAsync
            || reference.Method.GetAttributes().Any(static x => x.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"),
        _ => false,
    };

    /// <summary>
    /// The rule <c>DelegateJobBinding</c> applies, by exact type: the firing, its token, its scope, and
    /// a service for anything else.
    /// </summary>
    private static ArgumentSource SourceOf(ITypeSymbol type, Compilation compilation)
    {
        if (Is(type, compilation, "Quartz.IJobExecutionContext"))
        {
            return ArgumentSource.Context;
        }

        if (Is(type, compilation, "System.Threading.CancellationToken"))
        {
            return ArgumentSource.CancellationToken;
        }

        return Is(type, compilation, "System.IServiceProvider") ? ArgumentSource.Services : ArgumentSource.Service;
    }

    private static bool Is(ITypeSymbol type, Compilation compilation, string metadataName)
        => SymbolEqualityComparer.Default.Equals(type, compilation.GetTypeByMetadataName(metadataName));

    /// <summary>
    /// Whether a file of its own in this assembly can spell the type: what the generated cast and
    /// <c>typeof</c> have to do.
    /// </summary>
    /// <remarks>
    /// A type parameter is out of reach because an interceptor cannot be generic over the caller's type
    /// parameters when the method it replaces is not; a tuple because its element names are no part of
    /// the type the container is asked for, and a service registered as one is not worth the edge case.
    /// </remarks>
    private static bool IsNameable(ITypeSymbol type, Compilation compilation)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsNameable(array.ElementType, compilation);
            case INamedTypeSymbol named:
                if (named.IsAnonymousType
                    || named.IsTupleType
                    || named.TypeKind == TypeKind.Error
                    || !compilation.IsSymbolAccessibleWithin(named, compilation.Assembly))
                {
                    return false;
                }

                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (current.IsFileLocal || current.TypeArguments.Any(x => !IsNameable(x, compilation)))
                    {
                        return false;
                    }
                }

                return true;
            default:
                // A type parameter, a pointer, a function pointer, dynamic.
                return false;
        }
    }

    /// <summary>
    /// A type as the generated code spells it: fully qualified, and without nullable annotations, which
    /// <c>typeof</c> refuses and a cast does not need.
    /// </summary>
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// The call's file name and one-based line and column, for a reader of the generated file.
    /// </summary>
    private static string DisplayLocation(SyntaxNode name)
    {
        FileLinePositionSpan span = name.GetLocation().GetLineSpan();
        string path = span.Path ?? "";
        string file = path.Substring(path.LastIndexOfAny(['/', '\\']) + 1);

        return $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<DelegateJobCall> calls, bool enabled)
    {
        if (!enabled || calls.IsDefaultOrEmpty)
        {
            return;
        }

        context.AddSource("QuartzDelegateJobs.g.cs", DelegateJobsEmitter.Emit(calls));
    }
}
