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
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quartz.Analyzers;

/// <summary>
/// Turns the <c>[QuartzJob]</c> and <c>[CronTrigger]</c> attributes an assembly declares into the
/// registration calls an application would otherwise have written by hand.
/// </summary>
/// <remarks>
/// <para>
/// The attributes are matched by metadata name, so this generator needs no reference to
/// <c>Quartz.dll</c> — which it could not have, being loaded by the compiler. What it emits is
/// ordinary C# calling the public <c>AddJob&lt;T&gt;</c> and <c>AddTrigger&lt;T&gt;</c>: no
/// reflection, no <c>Type.GetType</c>, nothing that a trimmer or ILCompiler has to be told about.
/// </para>
/// <para>
/// One <c>QuartzDeclaredJobs</c> class per compilation, and internal, so that no assembly adds
/// anything to its public surface and two assemblies that both declare jobs do not collide. The one
/// exception is <c>InternalsVisibleTo</c>, which puts both in scope in the assembly it names; that
/// assembly's class is then named after it — see <see cref="NameRegistration" />.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class DeclaredJobsGenerator : IIncrementalGenerator
{
    private const string GeneratedTypeName = "Quartz." + RegistrationName.OrdinaryClassName;

    private const string QuartzJobAttributeTypeName = "Quartz.QuartzJobAttribute";

    private const string CronTriggerAttributeTypeName = "Quartz.CronTriggerAttribute";

    private const string JobInterfaceTypeName = "Quartz.IJob";

    /// <summary>
    /// The group a key with no group of its own falls into, mirroring <c>Key&lt;T&gt;.DefaultGroup</c>.
    /// </summary>
    /// <remarks>
    /// Used to tell two declarations apart and to name a key in a diagnostic, never emitted: the
    /// generated code leaves a defaulted group off the call and lets Quartz apply its own.
    /// </remarks>
    private const string DefaultGroup = "DEFAULT";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Types only. The attributes are matched by name, so one written on a method or on the
        // assembly still arrives here, with a target that is not a type; the compiler's CS0592 already
        // reports that, and the jobs declared where the attribute belongs are generated as ever.
        IncrementalValuesProvider<DeclaredJob> jobs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                QuartzJobAttributeTypeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (attributeContext, _) => ReadJob(attributeContext));

        IncrementalValuesProvider<OrphanTrigger> orphans = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CronTriggerAttributeTypeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (attributeContext, _) => ReadOrphan(attributeContext))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        // Read off the compilation, which is new on every edit, and reduced to strings at once, so that
        // an edit that changes nothing about what this assembly can see compares equal and re-emits nothing.
        IncrementalValueProvider<RegistrationName> registration = context.CompilationProvider
            .Select(static (compilation, _) => NameRegistration(compilation));

        context.RegisterSourceOutput(
            jobs.Collect().Combine(orphans.Collect()).Combine(registration),
            static (production, source) => Execute(production, source.Left.Left, source.Left.Right, source.Right));
    }

    /// <summary>
    /// What this assembly's registration is called: <c>QuartzDeclaredJobs.AddDeclaredJobs</c>, unless
    /// another assembly's class of that name is already visible here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every assembly that declares jobs gets an internal <c>Quartz.QuartzDeclaredJobs</c>, and being
    /// internal keeps them apart until one assembly grants another <c>InternalsVisibleTo</c>. Then both
    /// are in scope in the second, and <c>AddDeclaredJobs()</c> is ambiguous there with no spelling
    /// that resolves it: naming the class is ambiguous too. So the second assembly's class steps aside
    /// and is named after that assembly, and the ordinary name keeps meaning the one it could already see.
    /// </para>
    /// <para>
    /// A renamed class is never what another assembly looks for here, so a chain of grants renames
    /// each assembly at most once, and only the ones that can see an ordinary name.
    /// </para>
    /// </remarks>
    private static RegistrationName NameRegistration(Compilation compilation)
    {
        string? visible = null;

        foreach (INamedTypeSymbol type in compilation.GetTypesByMetadataName(GeneratedTypeName))
        {
            if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                continue;
            }

            // The first by name, so that the warning names the same assembly however the references
            // happened to be ordered.
            string name = type.ContainingAssembly.Name;
            if (visible is null || string.CompareOrdinal(name, visible) < 0)
            {
                visible = name;
            }
        }

        if (visible is null)
        {
            return RegistrationName.Ordinary;
        }

        string suffix = Identifier(compilation.Assembly.Name);

        return new RegistrationName(
            RegistrationName.OrdinaryClassName + "_" + suffix,
            RegistrationName.OrdinaryMethodName + "From" + suffix,
            visible);
    }

    /// <summary>
    /// An assembly name made into a C# identifier: every character an identifier cannot hold becomes
    /// <c>_</c>, and one that cannot start an identifier is given a <c>_</c> to follow.
    /// </summary>
    private static string Identifier(string assemblyName)
    {
        StringBuilder identifier = new StringBuilder(assemblyName.Length + 1);

        foreach (char character in assemblyName)
        {
            identifier.Append(SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_');
        }

        if (!SyntaxFacts.IsIdentifierStartCharacter(identifier[0]))
        {
            identifier.Insert(0, '_');
        }

        return identifier.ToString();
    }

    /// <summary>
    /// Everything one <c>[QuartzJob]</c> class says, or why it says nothing that can be registered.
    /// </summary>
    private static DeclaredJob ReadJob(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol) context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        LocationInfo? location = LocationInfo.From(attribute.ApplicationSyntaxReference);
        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string displayName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        JobProblem problem = Validate(type, context.SemanticModel.Compilation);
        if (problem != JobProblem.None)
        {
            return new DeclaredJob(
                typeName,
                displayName,
                problem,
                type.Name,
                Group: null,
                Description: null,
                Durable: false,
                RequestRecovery: false,
                Scheduler: null,
                new EquatableArray<DeclaredTrigger>(ImmutableArray<DeclaredTrigger>.Empty),
                location);
        }

        string name = Text(attribute, nameof(DeclaredJob.Name)) ?? type.Name;
        string? group = Text(attribute, nameof(DeclaredJob.Group));

        ImmutableArray<DeclaredTrigger> triggers = ReadTriggers(type, name, group);

        return new DeclaredJob(
            typeName,
            displayName,
            JobProblem.None,
            name,
            group,
            Text(attribute, nameof(DeclaredJob.Description)),
            // A job nothing points at is deleted as soon as it is stored unless it is durable, so a
            // job that declares no schedule is stored durably whatever the attribute says. Declaring
            // one that vanishes could not be what the attribute was written for.
            Durable: Flag(attribute, nameof(DeclaredJob.Durable)) || triggers.Length == 0,
            Flag(attribute, nameof(DeclaredJob.RequestRecovery)),
            Text(attribute, nameof(DeclaredJob.Scheduler)),
            new EquatableArray<DeclaredTrigger>(triggers),
            location);
    }

    /// <summary>
    /// Every <c>[CronTrigger]</c> on the job, in the order they are written.
    /// </summary>
    private static ImmutableArray<DeclaredTrigger> ReadTriggers(INamedTypeSymbol type, string jobName, string? jobGroup)
    {
        ImmutableArray<DeclaredTrigger>.Builder builder = ImmutableArray.CreateBuilder<DeclaredTrigger>();
        int index = 0;

        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != CronTriggerAttributeTypeName)
            {
                continue;
            }

            index++;

            string expression = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string ?? ""
                : "";

            // The first schedule a job declares is named after the job, because that is what a single
            // trigger would have been called by hand; the rest count up from there.
            string name = Text(attribute, nameof(DeclaredTrigger.Name))
                ?? (index == 1 ? jobName : jobName + "-" + index.ToString(CultureInfo.InvariantCulture));

            (int misfire, string? misfireName) = Misfire(attribute);

            builder.Add(new DeclaredTrigger(
                expression,
                name,
                Text(attribute, nameof(DeclaredTrigger.Group)) ?? jobGroup,
                Text(attribute, nameof(DeclaredTrigger.TimeZone)),
                misfire,
                misfireName,
                Number(attribute, nameof(DeclaredTrigger.Priority)),
                Text(attribute, nameof(DeclaredTrigger.Description)),
                Text(attribute, nameof(DeclaredTrigger.ExecutionGroup)),
                LocationInfo.From(attribute.ApplicationSyntaxReference)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// A <c>[CronTrigger]</c> on a class carrying no <c>[QuartzJob]</c>, which declares a schedule
    /// for a job that does not exist.
    /// </summary>
    private static OrphanTrigger? ReadOrphan(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol) context.TargetSymbol;

        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == QuartzJobAttributeTypeName)
            {
                return null;
            }
        }

        // One report per class rather than one per schedule: the fix is the missing attribute, and it
        // is missing once however many schedules were written under it.
        return new OrphanTrigger(
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            LocationInfo.From(context.Attributes[0].ApplicationSyntaxReference));
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<DeclaredJob> jobs,
        ImmutableArray<OrphanTrigger> orphans,
        RegistrationName registration)
    {
        foreach (OrphanTrigger orphan in orphans.OrderBy(x => x.DisplayName, StringComparer.Ordinal))
        {
            Report(context, Descriptors.CronTriggerWithoutQuartzJob, orphan.Location, orphan.DisplayName);
        }

        List<DeclaredJob> declared = [];

        // By type name, so that the generated file is the same however the compiler happened to order
        // the syntax trees it read.
        foreach (DeclaredJob job in jobs.OrderBy(x => x.TypeName, StringComparer.Ordinal))
        {
            if (job.Problem != JobProblem.None)
            {
                Report(context, Descriptors.DeclaredJobTypeNotSchedulable, job.Location, job.DisplayName, Explain(job.Problem));
                continue;
            }

            declared.Add(job);
        }

        ReportDuplicates(context, declared);

        if (declared.Count == 0)
        {
            // Nothing declared anything, so this assembly gets no AddDeclaredJobs to call. A
            // compilation that never heard of the attributes is left exactly as it was.
            return;
        }

        if (registration.VisibleAssembly is not null)
        {
            // Once, on the first job: the rename is a fact about the assembly, not about any one job.
            Report(context, Descriptors.DeclaredJobsRegistrationRenamed, declared[0].Location, registration.VisibleAssembly, registration.MethodName);
        }

        context.AddSource("QuartzDeclaredJobs.g.cs", DeclaredJobsEmitter.Emit(declared, registration));
    }

    /// <summary>
    /// Two declarations resolving to one key, which is a job overwriting a job or a trigger
    /// overwriting a trigger the moment the scheduler is built.
    /// </summary>
    /// <remarks>
    /// Keys are compared within a scheduler, because that is the scope they are unique in: the same
    /// key on two schedulers is two jobs, and a <c>Scheduler</c> on the attribute is how an
    /// application says so.
    /// </remarks>
    private static void ReportDuplicates(SourceProductionContext context, List<DeclaredJob> declared)
    {
        HashSet<string> jobKeys = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> triggerKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (DeclaredJob job in declared)
        {
            if (!jobKeys.Add(Identity(job.Scheduler, job.Group, job.Name)))
            {
                Report(context, Descriptors.DuplicateDeclaredIdentity, job.Location, "job", Display(job.Group, job.Name));
            }

            foreach (DeclaredTrigger trigger in job.Triggers)
            {
                if (!triggerKeys.Add(Identity(job.Scheduler, trigger.Group, trigger.Name)))
                {
                    Report(context, Descriptors.DuplicateDeclaredIdentity, trigger.Location, "trigger", Display(trigger.Group, trigger.Name));
                }
            }
        }
    }

    private static string Identity(string? scheduler, string? group, string name)
        => (scheduler ?? "") + "\u0000" + (group ?? DefaultGroup) + "\u0000" + name;

    private static string Display(string? group, string name) => (group ?? DefaultGroup) + "." + name;

    private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, LocationInfo? location, params object?[] arguments)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location?.ToLocation(), arguments));
    }

    /// <summary>
    /// Whether the generated registration could name this type at all, and construct it once it had.
    /// </summary>
    private static JobProblem Validate(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? job = compilation.GetTypeByMetadataName(JobInterfaceTypeName);

        // IJob<TInput> inherits IJob, so one question answers for both shapes of job.
        if (job is null || !type.AllInterfaces.Any(x => SymbolEqualityComparer.Default.Equals(x.OriginalDefinition, job)))
        {
            return JobProblem.NotAJob;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return JobProblem.Generic;
            }
        }

        if (type.IsAbstract)
        {
            return JobProblem.Abstract;
        }

        return IsAccessible(type) ? JobProblem.None : JobProblem.Inaccessible;
    }

    /// <summary>
    /// Whether another file in the same assembly can name the type.
    /// </summary>
    /// <remarks>
    /// The generated registration is a file of its own, so a <c>private</c> or <c>protected</c>
    /// nested class and a <c>file</c>-local one are out of its reach however visible they are where
    /// they are written.
    /// </remarks>
    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }

            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static string Explain(JobProblem problem) => problem switch
    {
        JobProblem.NotAJob => "does not implement IJob",
        JobProblem.Generic => "is generic",
        JobProblem.Abstract => "is abstract",
        _ => "cannot be named from another file in this assembly",
    };

    /// <summary>
    /// A named string argument, or <see langword="null" /> when it was not written — or was written
    /// blank, which is the same thing said less clearly.
    /// </summary>
    private static string? Text(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool Flag(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    /// <summary>
    /// A named <see cref="int" /> argument, or the priority every trigger has when none is written.
    /// </summary>
    private static int Number(AttributeData attribute, string name)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value)
            {
                return value;
            }
        }

        return TriggerConstants.DefaultPriority;
    }

    /// <summary>
    /// The misfire instruction's value and the enum member it is.
    /// </summary>
    /// <remarks>
    /// The member's name is read off the enum rather than spelled here, so the generated call says
    /// what the application wrote and a member added to <c>CronTriggerMisfireInstruction</c> needs no
    /// change in this file. A value that names no member is emitted as the cast it was written as.
    /// </remarks>
    private static (int Value, string? Name) Misfire(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key != nameof(DeclaredTrigger.MisfireInstruction) || argument.Value.Value is not int value)
            {
                continue;
            }

            if (argument.Value.Type is not INamedTypeSymbol enumeration)
            {
                return (value, null);
            }

            foreach (ISymbol member in enumeration.GetMembers())
            {
                if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, value))
                {
                    return (value, field.Name);
                }
            }

            return (value, null);
        }

        return (0, null);
    }
}
