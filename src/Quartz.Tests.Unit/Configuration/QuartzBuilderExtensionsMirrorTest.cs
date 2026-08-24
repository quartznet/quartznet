using System.Reflection;
using System.Runtime.CompilerServices;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Every <see cref="IQuartzBuilder" /> extension has to be mirrored as an instance method on
/// <see cref="QuartzSchedulerBuilder" />, or a standalone builder's chain stops being a
/// <see cref="QuartzSchedulerBuilder" /> at that call and cannot reach <c>Build()</c>.
/// </summary>
/// <remarks>
/// An extension method cannot preserve the receiver's type here. Making it generic in the receiver —
/// <c>AddJob&lt;TBuilder, TJob&gt;</c>, or an <c>extension&lt;TBuilder&gt;</c> block — breaks the call
/// sites that name the job type, because C# has no partial type-argument inference: given
/// <c>AddJob&lt;MyJob&gt;(…)</c> it cannot infer the builder and take the job type. So the mirror is the
/// mechanism, and this test is what keeps it complete.
/// </remarks>
public class QuartzBuilderExtensionsMirrorTest
{
    [Test]
    public void EveryBuilderExtensionIsMirroredOnTheStandaloneBuilder()
    {
        List<string> missing = [];

        foreach (MethodInfo extension in Extensions())
        {
            string signature = Describe(extension);
            bool mirrored = typeof(QuartzSchedulerBuilder)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => candidate.Name == extension.Name)
                .Any(candidate => Describe(candidate) == signature);

            if (!mirrored)
            {
                missing.Add(signature);
            }
        }

        missing.Should().BeEmpty(
            "a QuartzSchedulerBuilder chain returns IQuartzBuilder — and stops being buildable — at the "
            + "first extension it has no instance method for");
    }

    [Test]
    public void EveryMirrorReturnsTheBuilderItself()
    {
        List<MethodInfo> mirrors = Mirrors();

        mirrors.Should().NotBeEmpty("the mirrors are what this fixture is about");
        mirrors.Should().OnlyContain(method => method.ReturnType == typeof(QuartzSchedulerBuilder),
            "returning the interface would end the chain just as the extension did");
    }

    /// <summary>
    /// Shape is not behaviour: a mirror that returns the builder without calling the extension passes
    /// both tests above and registers nothing. <see cref="QuartzSchedulerBuilderForwardingTest" /> calls
    /// each one and reads the registration back, so what this checks is that its case list has not
    /// fallen behind — an overload added without a case would be a member nothing invokes.
    /// </summary>
    [Test]
    public void EveryMirrorHasAForwardingCase()
    {
        Dictionary<string, int> mirrored = Mirrors()
            .GroupBy(method => method.Name)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<string, int> covered = QuartzSchedulerBuilderForwardingTest.All
            .GroupBy(testCase => testCase.Member)
            .ToDictionary(group => group.Key, group => group.Count());

        covered.Should().BeEquivalentTo(mirrored,
            "every mirror needs a case that invokes it; the counts are per name, so a new overload of an "
            + "existing one cannot slip in behind the cases already there");
    }

    private static List<MethodInfo> Mirrors()
    {
        return typeof(QuartzSchedulerBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => Extensions().Any(extension => extension.Name == method.Name))
            .ToList();
    }

    private static List<MethodInfo> Extensions()
    {
        return typeof(QuartzBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            .Where(method => method.GetParameters()[0].ParameterType == typeof(IQuartzBuilder))
            .ToList();
    }

    /// <summary>
    /// The name, the type-parameter count and the parameter types, with the receiver dropped from an
    /// extension so the two shapes are comparable. Type parameters print by name, and the mirrors keep
    /// the extensions' names, so a genuine mirror compares equal.
    /// </summary>
    private static string Describe(MethodInfo method)
    {
        IEnumerable<ParameterInfo> parameters = method.GetParameters();
        if (method.IsStatic)
        {
            parameters = parameters.Skip(1);
        }

        string arity = method.IsGenericMethodDefinition ? $"`{method.GetGenericArguments().Length}" : "";
        return $"{method.Name}{arity}({string.Join(", ", parameters.Select(parameter => parameter.ParameterType.ToString()))})";
    }
}
