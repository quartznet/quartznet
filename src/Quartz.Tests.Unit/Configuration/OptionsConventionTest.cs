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

#nullable enable

using System.Collections;
using System.Reflection;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The mechanical half of the S6 options rule, which the 4.0 API finalization ratified as a
/// principle and asked to be enforced "by a reflection test beside the API baselines".
/// </summary>
/// <remarks>
/// <para>
/// The rule asks one question — <em>who calls <c>new</c>?</em> — and every other clause follows
/// from the answer.
/// </para>
/// <para>
/// <strong>Group A, container-bound options.</strong> Quartz calls <c>new</c>: the instance is
/// created by <c>Microsoft.Extensions.Options</c>, or by a registration helper immediately before it
/// invokes the caller's <c>Action&lt;T&gt;</c>, and is then mutated by that delegate or bound from
/// <c>IConfiguration</c>. So: a sealed class with a public parameterless constructor; every scalar
/// member <c>{ get; set; }</c> carrying the recommended value as its initialiser; every collection
/// or nested-options member <c>{ get; }</c> initialised in place, because the binder binds
/// <em>into</em> the existing instance and a setter lets one <c>Configure</c> callback silently
/// discard another's edits; never a record, never <c>init</c>, never <c>required</c>, never a
/// struct — <c>required</c> cannot be honoured when the application never runs a constructor.
/// </para>
/// <para>
/// <strong>Group B, call-site arguments.</strong> The application calls <c>new</c>, writes an object
/// initialiser and passes the value to a Quartz method; the type is never in the container. So: a
/// <c>readonly record struct</c> with every member <c>init</c>, and defaults chosen so that
/// <c>default</c> <em>is</em> the conservative behaviour — which is what lets the parameter be
/// <c>T options = default</c> rather than <c>T? options = null</c>, and is what removed four
/// null-normalising sites when the shape landed.
/// </para>
/// <para>
/// Two clauses of the rule are deliberately not encoded here, because encoding them would mean
/// inventing policy rather than enforcing it. "Defaults encode recommended usage" is a judgement
/// about values, not shapes; the closest mechanical statement — that a fresh instance is readable
/// and its complex members are already there — <em>is</em> checked. And "validated by an
/// <c>IValidateOptions&lt;T&gt;</c>" is not yet true of the whole set: four of the container-bound
/// types have nothing to validate, and <c>ClusteringStaysEnabledValidator</c> is registered at its
/// own use site rather than centrally.
/// </para>
/// <para>
/// Scope is the <c>Quartz</c> assembly's exported types. The satellite packages carry options of
/// their own — including <c>QuartzHealthCheckOptions</c>, whose <c>Tags</c> setter the finalization
/// campaign flagged for an S6 re-check — and they are not covered here.
/// </para>
/// </remarks>
public class OptionsConventionTest
{
    private static readonly Assembly quartzAssembly = typeof(IScheduler).Assembly;

    /// <summary>
    /// Members the referenced-type rule cannot express, each with the reason it is out of reach.
    /// A new entry needs a reason of the same kind: a contract imposed from outside Quartz.
    /// </summary>
    private static readonly Dictionary<string, string> referencedTypeExceptions = new(StringComparer.Ordinal)
    {
        // Ratified by A5's S6 audit, which lists DataSourceOptions among the fully conforming types:
        // a keyed-service key is typed 'object' by Microsoft.Extensions.DependencyInjection's own
        // contract (ServiceDescriptor.ServiceKey, [FromKeyedServices(object)]), so an option that
        // carries one to the container cannot be narrower than the container itself.
        ["Quartz.DataSourceOptions.DataSourceServiceKey"] = "a DI service key is typed 'object' by the container's contract"
    };

    /// <summary>
    /// Types a scalar member may be: assigned wholesale, carrying no state Quartz binds into.
    /// </summary>
    private static readonly HashSet<Type> scalarTypes =
    [
        typeof(string), typeof(decimal), typeof(TimeSpan), typeof(DateTime), typeof(DateTimeOffset),
        typeof(TimeOnly), typeof(DateOnly), typeof(Guid), typeof(Uri), typeof(Type), typeof(Version)
    ];

    /// <summary>
    /// The options types the application constructs itself, discovered the way the rule defines them:
    /// they appear as a parameter of a public Quartz member. Members declared on an options type are
    /// skipped so that a record's own generated <c>Equals(T)</c> cannot classify its declaring type.
    /// </summary>
    private static readonly HashSet<Type> callSiteArgumentOptions = FindCallSiteArgumentOptions();

    public static IEnumerable<Type> AllOptions() => quartzAssembly.GetExportedTypes()
        .Where(IsOptionsType)
        .OrderBy(type => type.FullName, StringComparer.Ordinal);

    public static IEnumerable<Type> ContainerBoundOptions() => AllOptions().Where(type => !callSiteArgumentOptions.Contains(type));

    public static IEnumerable<Type> CallSiteArgumentOptions() => AllOptions().Where(callSiteArgumentOptions.Contains);

    [Test]
    public void TheAssemblyHasOptionsTypesOfBothKinds()
    {
        // Guards the guards: a classification that silently found nothing would make every test below
        // pass without checking anything.
        ContainerBoundOptions().Should().NotBeEmpty();
        CallSiteArgumentOptions().Should().NotBeEmpty();
    }

    [TestCaseSource(nameof(AllOptions))]
    public void EveryOptionsTypeIsSealed(Type optionsType)
    {
        optionsType.IsSealed.Should().BeTrue(
            "an options type is data, and nothing in Quartz calls a virtual member on one — subclassing "
            + "it would only produce an instance the binder cannot fill");
    }

    [TestCaseSource(nameof(AllOptions))]
    public void NoOptionsTypeExposesAnInstanceField(Type optionsType)
    {
        FieldInfo[] fields = optionsType.GetFields(BindingFlags.Public | BindingFlags.Instance);

        fields.Select(field => field.Name).Should().BeEmpty(
            "the configuration binder writes properties, so a public field is a setting that binds from "
            + "nowhere; constants and statics are fine and are not instance state");
    }

    [Test]
    public void EveryReferencedTypeExceptionIsStillEarningItsPlace()
    {
        List<PropertyInfo> properties = AllOptions().SelectMany(PublicProperties).ToList();

        foreach (string member in referencedTypeExceptions.Keys)
        {
            PropertyInfo? property = properties.SingleOrDefault(candidate => Describe(candidate) == member);

            property.Should().NotBeNull($"the allow-list names {member}, which no longer exists");
            ShapeOf(Unwrap(property!.PropertyType)).Should().Be(MemberShape.Unknown,
                $"{member} would pass the rule on its own now, so its exception is stale and should go");
        }
    }

    [TestCaseSource(nameof(AllOptions))]
    public void EveryOptionsMemberIsTypedAsAnOptionsTypeAnEnumOrABclType(Type optionsType)
    {
        List<string> violations = [];
        foreach (PropertyInfo property in PublicProperties(optionsType))
        {
            if (ShapeOf(property) == MemberShape.Unknown)
            {
                violations.Add($"{Describe(property)} is a {property.PropertyType.Name}");
            }
        }

        violations.Should().BeEmpty(
            "an options graph is reachable from configuration, so it may only name other options types, "
            + "enums, delegates and BCL types — a Quartz component reached through an option is a "
            + "service that belongs in the container instead");
    }

    // ---------------------------------------------------------------------------------------------
    // Group A — Quartz calls new
    // ---------------------------------------------------------------------------------------------

    [TestCaseSource(nameof(ContainerBoundOptions))]
    public void ContainerBoundOptionsAreClassesTheBinderCanConstruct(Type optionsType)
    {
        optionsType.IsValueType.Should().BeFalse("the container hands the same instance to every Configure callback");
        IsRecord(optionsType).Should().BeFalse(
            "a record's value semantics say the instance is replaced, and a container-bound instance is mutated in place");

        ConstructorInfo[] constructors = optionsType.GetConstructors();
        constructors.Should().ContainSingle("the binder picks a constructor by having only one to pick")
            .Which.GetParameters().Should().BeEmpty(
                "nothing supplies constructor arguments: Microsoft.Extensions.Options creates the instance");
    }

    [TestCaseSource(nameof(ContainerBoundOptions))]
    public void ContainerBoundOptionsHaveNoInitOnlyOrRequiredMembers(Type optionsType)
    {
        List<string> violations = [];
        foreach (PropertyInfo property in PublicProperties(optionsType))
        {
            if (IsInitOnly(property))
            {
                violations.Add($"{Describe(property)} is init-only");
            }

            if (IsRequired(property))
            {
                violations.Add($"{Describe(property)} is required");
            }
        }

        violations.Should().BeEmpty(
            "the application never runs a constructor or an object initialiser for these, so init-only "
            + "shuts the Configure callback out and required can never be satisfied — a mandatory value "
            + "is a non-nullable property plus an IValidateOptions");
    }

    [TestCaseSource(nameof(ContainerBoundOptions))]
    public void ContainerBoundScalarsAreSettableAndComplexMembersAreGetOnly(Type optionsType)
    {
        List<string> violations = [];
        foreach (PropertyInfo property in PublicProperties(optionsType))
        {
            bool readable = property.GetMethod is { IsPublic: true };
            bool writable = property.SetMethod is { IsPublic: true };

            if (!readable)
            {
                violations.Add($"{Describe(property)} has no public getter");
                continue;
            }

            switch (ShapeOf(property))
            {
                case MemberShape.Scalar when !writable:
                    violations.Add($"{Describe(property)} is a scalar with no setter");
                    break;
                case MemberShape.Complex when writable:
                    violations.Add($"{Describe(property)} is a collection or nested options object with a setter");
                    break;
            }
        }

        violations.Should().BeEmpty(
            "a scalar is assigned wholesale, so it is get/set; a collection or nested options object is "
            + "bound into, so it is get-only — a setter there lets one Configure callback replace the "
            + "instance another one had already written to");
    }

    [TestCaseSource(nameof(ContainerBoundOptions))]
    public void ContainerBoundComplexMembersAreInitialisedInPlace(Type optionsType)
    {
        object instance = Activator.CreateInstance(optionsType)!;

        List<string> violations = [];
        foreach (PropertyInfo property in PublicProperties(optionsType).Where(p => ShapeOf(p) == MemberShape.Complex))
        {
            object? value = property.GetValue(instance);
            if (value is null)
            {
                violations.Add($"{Describe(property)} is null on a fresh instance");
                continue;
            }

            bool frozen = value switch
            {
                IDictionary dictionary => dictionary.IsReadOnly,
                IList list => list.IsReadOnly,
                _ => false
            };

            if (frozen)
            {
                violations.Add($"{Describe(property)} is read-only on a fresh instance");
            }
        }

        violations.Should().BeEmpty(
            "the binder and every Configure callback write into whatever is already there, so a get-only "
            + "member that starts out null or frozen silently swallows the configuration aimed at it");
    }

    // ---------------------------------------------------------------------------------------------
    // Group B — the application calls new
    // ---------------------------------------------------------------------------------------------

    [TestCaseSource(nameof(CallSiteArgumentOptions))]
    public void CallSiteArgumentOptionsAreReadonlyRecordStructs(Type optionsType)
    {
        optionsType.IsValueType.Should().BeTrue(
            "a value passed to a method should not make the caller allocate, and should not be aliased "
            + "by the callee afterwards");

        optionsType.GetCustomAttributesData().Should().Contain(
            attribute => attribute.AttributeType.Name == "IsReadOnlyAttribute",
            "readonly is what stops a member mutating a copy nobody reads back");

        IsRecord(optionsType).Should().BeTrue(
            "value equality and a printable ToString are free, and a call-site argument is compared and "
            + "logged far more often than it is mutated");
    }

    [TestCaseSource(nameof(CallSiteArgumentOptions))]
    public void CallSiteArgumentOptionMembersAreInitOnly(Type optionsType)
    {
        List<string> violations = [];
        foreach (PropertyInfo property in PublicProperties(optionsType))
        {
            if (property.GetMethod is not { IsPublic: true })
            {
                violations.Add($"{Describe(property)} has no public getter");
            }

            if (property.SetMethod is not { IsPublic: true })
            {
                violations.Add($"{Describe(property)} has no initialiser");
            }
            else if (!IsInitOnly(property))
            {
                violations.Add($"{Describe(property)} has a plain setter");
            }
        }

        violations.Should().BeEmpty(
            "the whole value is written in one object initialiser at the call site and read by Quartz "
            + "afterwards; a plain setter suggests a lifetime the value does not have");
    }

    [Test]
    public void CallSiteArgumentOptionsAreNeverBoundThroughTheOptionsPattern()
    {
        List<string> violations = [];
        foreach (Type type in ConsumingTypes())
        {
            foreach (MethodBase member in PublicMembers(type))
            {
                foreach (ParameterInfo parameter in member.GetParameters())
                {
                    foreach (Type argument in OptionsPatternArguments(parameter.ParameterType))
                    {
                        if (callSiteArgumentOptions.Contains(argument))
                        {
                            violations.Add($"{type.Name}.{member.Name} binds {argument.Name} as {parameter.ParameterType.Name}");
                        }
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "a call-site argument is never registered in a container: the corollary of the rule is that "
            + "the two groups do not overlap, and a type configured through Action<T> is group A");
    }

    [Test]
    public void CallSiteArgumentParametersDefaultToTheConservativeValue()
    {
        List<string> violations = [];
        foreach (Type type in ConsumingTypes())
        {
            foreach (MethodBase member in PublicMembers(type))
            {
                ParameterInfo[] parameters = member.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];
                    Type parameterType = Unwrap(parameter.ParameterType);
                    if (!callSiteArgumentOptions.Contains(parameterType))
                    {
                        continue;
                    }

                    if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
                    {
                        violations.Add($"{type.Name}.{member.Name} takes {parameterType.Name}? — the absent value has a spelling of its own");
                        continue;
                    }

                    // A parameter cannot be optional while something after it is required, which is the
                    // one reason the rule tolerates a mandatory options argument.
                    bool couldBeOptional = parameters.Skip(i + 1).All(later => later.IsOptional);
                    if (couldBeOptional && !parameter.IsOptional)
                    {
                        violations.Add($"{type.Name}.{member.Name} takes a mandatory {parameterType.Name}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "'default' is the conservative behaviour by construction, so the parameter is 'T options = "
            + "default'; 'T? options = null' would put the same meaning in two spellings and make every "
            + "callee normalise it");
    }

    // ---------------------------------------------------------------------------------------------

    private enum MemberShape
    {
        /// <summary>Assigned wholesale by a Configure callback.</summary>
        Scalar,

        /// <summary>A collection or a nested options object, which the binder writes into.</summary>
        Complex,

        /// <summary>Something the rule does not allow an options type to name at all.</summary>
        Unknown
    }

    private static MemberShape ShapeOf(PropertyInfo property) =>
        referencedTypeExceptions.ContainsKey(Describe(property))
            ? MemberShape.Scalar
            : ShapeOf(Unwrap(property.PropertyType));

    private static MemberShape ShapeOf(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || scalarTypes.Contains(type))
        {
            return MemberShape.Scalar;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            // A delegate has no state to bind into; it is replaced, never edited.
            return MemberShape.Scalar;
        }

        if (IsOptionsType(type) || typeof(IEnumerable).IsAssignableFrom(type))
        {
            return MemberShape.Complex;
        }

        return MemberShape.Unknown;
    }

    private static HashSet<Type> FindCallSiteArgumentOptions()
    {
        HashSet<Type> found = [];
        foreach (Type type in ConsumingTypes())
        {
            foreach (MethodBase member in PublicMembers(type))
            {
                foreach (ParameterInfo parameter in member.GetParameters())
                {
                    Type parameterType = Unwrap(parameter.ParameterType);
                    if (IsOptionsType(parameterType))
                    {
                        found.Add(parameterType);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The options types a parameter carries into the container: <c>Action&lt;T&gt;</c> and the
    /// <c>Microsoft.Extensions.Options</c> family are how a group-A type is configured.
    /// </summary>
    private static IEnumerable<Type> OptionsPatternArguments(Type parameterType)
    {
        Type type = Unwrap(parameterType);
        if (!type.IsGenericType)
        {
            yield break;
        }

        Type definition = type.GetGenericTypeDefinition();
        bool optionsPattern = definition == typeof(Action<>)
            || string.Equals(definition.Namespace, "Microsoft.Extensions.Options", StringComparison.Ordinal);

        if (!optionsPattern)
        {
            yield break;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            yield return argument;
        }
    }

    /// <summary>
    /// The exported types that <em>use</em> options rather than being one. An options type's own
    /// generated equality members take it as a parameter, which would classify every record as a
    /// call-site argument if they were counted.
    /// </summary>
    private static IEnumerable<Type> ConsumingTypes() => quartzAssembly.GetExportedTypes().Where(type => !IsOptionsType(type));

    private static IEnumerable<MethodBase> PublicMembers(Type type) => type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Cast<MethodBase>()
        .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

    private static IEnumerable<PropertyInfo> PublicProperties(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static bool IsOptionsType(Type type) =>
        type.Assembly == quartzAssembly && type.IsPublic && type.Name.EndsWith("Options", StringComparison.Ordinal);

    private static Type Unwrap(Type type)
    {
        Type unwrapped = type.IsByRef ? type.GetElementType()! : type;
        return Nullable.GetUnderlyingType(unwrapped) ?? unwrapped;
    }

    /// <summary>
    /// Records — class and struct alike — get a compiler-generated <c>PrintMembers</c>; nothing else does.
    /// </summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    private static bool IsInitOnly(PropertyInfo property) => property.SetMethod is { } setter
        && setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsExternalInit");

    private static bool IsRequired(PropertyInfo property) => property.GetCustomAttributesData()
        .Any(attribute => attribute.AttributeType.Name == "RequiredMemberAttribute");

    private static string Describe(PropertyInfo property) => $"{property.DeclaringType!.FullName}.{property.Name}";
}
