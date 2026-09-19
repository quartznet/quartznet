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
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quartz.Analyzers;

/// <summary>
/// Finds a job that re-stores its data map when a firing completes and lets two firings run at once.
/// </summary>
/// <remarks>
/// <para>
/// Both attributes are read the way <c>JobDetailImpl</c> reads them: the type's own declaration, a
/// base class it inherits from, or any interface it implements - a job is allowed to take either
/// attribute from a contract rather than declaring it. So a class whose interface says
/// <c>[DisallowConcurrentExecution]</c> is not reported, and a class that carries neither but
/// implements an interface carrying only <c>[PersistJobDataAfterExecution]</c> is.
/// </para>
/// <para>
/// The question is asked of job types alone - the ones that implement <see cref="IJob" />, which is
/// what a scheduler can be handed - rather than of every type carrying the attribute. An interface
/// that declares <c>[PersistJobDataAfterExecution]</c> for its implementers to inherit is not itself
/// a job and has no firings to serialise, and warning about it as well would charge one design three
/// diagnostics for a fix that belongs in one place.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JobAttributesAnalyzer : DiagnosticAnalyzer
{
    private const string PersistJobDataAfterExecutionAttributeTypeName = "Quartz.PersistJobDataAfterExecutionAttribute";

    private const string DisallowConcurrentExecutionAttributeTypeName = "Quartz.DisallowConcurrentExecutionAttribute";

    private const string JobInterfaceTypeName = "Quartz.IJob";

    private const string GenericJobInterfaceTypeName = "Quartz.IJob`1";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.PersistJobDataWithoutDisallowConcurrent);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            INamedTypeSymbol? persist = compilationStart.Compilation.GetTypeByMetadataName(PersistJobDataAfterExecutionAttributeTypeName);
            INamedTypeSymbol? disallow = compilationStart.Compilation.GetTypeByMetadataName(DisallowConcurrentExecutionAttributeTypeName);
            INamedTypeSymbol? job = compilationStart.Compilation.GetTypeByMetadataName(JobInterfaceTypeName);
            INamedTypeSymbol? genericJob = compilationStart.Compilation.GetTypeByMetadataName(GenericJobInterfaceTypeName);

            if (persist is null || disallow is null || job is null)
            {
                // The compilation does not reference Quartz, so nothing here can be about Quartz.
                return;
            }

            compilationStart.RegisterSymbolAction(
                symbolContext => Analyze(symbolContext, persist, disallow, job, genericJob),
                SymbolKind.NamedType);
        });
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol persist,
        INamedTypeSymbol disallow,
        INamedTypeSymbol job,
        INamedTypeSymbol? genericJob)
    {
        INamedTypeSymbol type = (INamedTypeSymbol) context.Symbol;

        if (!IsJob(type, job, genericJob))
        {
            return;
        }

        if (!Carries(type, persist) || Carries(type, disallow))
        {
            return;
        }

        foreach (Location location in type.Locations)
        {
            if (location.IsInSource)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.PersistJobDataWithoutDisallowConcurrent,
                    location,
                    type.Name));

                // A partial type is one type however many files declare it; one report, on the first.
                return;
            }
        }
    }

    /// <summary>
    /// Whether a scheduler could be handed this type as a job.
    /// </summary>
    private static bool IsJob(INamedTypeSymbol type, INamedTypeSymbol job, INamedTypeSymbol? genericJob)
    {
        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            INamedTypeSymbol definition = implemented.OriginalDefinition;

            if (SymbolEqualityComparer.Default.Equals(definition, job)
                || (genericJob is not null && SymbolEqualityComparer.Default.Equals(definition, genericJob)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type, anything it inherits from, or any interface it implements carries the
    /// attribute.
    /// </summary>
    /// <remarks>
    /// <see cref="INamedTypeSymbol.AllInterfaces" /> is already flat - it reports the interfaces an
    /// interface itself inherits - which is what <c>Type.GetInterfaces()</c> gives the run-time
    /// lookup this mirrors.
    /// </remarks>
    private static bool Carries(INamedTypeSymbol type, INamedTypeSymbol attribute)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (HasAttribute(current, attribute))
            {
                return true;
            }
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (HasAttribute(implemented, attribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol attribute)
    {
        foreach (AttributeData data in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute))
            {
                return true;
            }
        }

        return false;
    }
}
