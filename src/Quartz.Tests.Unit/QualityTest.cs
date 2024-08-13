using System.Reflection;
using System.Runtime.CompilerServices;

namespace Quartz.Tests.Unit;

/// <summary>
/// http://haacked.com/archive/2014/11/11/async-void-methods/
/// </summary>
[TestFixture]
public class QualityTest
{
    [Test]
    public void EnsureNoAsyncVoidMethods()
    {
        AssertNoAsyncVoidMethods(GetType().Assembly);
        AssertNoAsyncVoidMethods(typeof(IJob).Assembly);
        // AssertNoAsyncVoidMethods(typeof (TriggersController).Assembly);
    }

    private static void AssertNoAsyncVoidMethods(Assembly assembly)
    {
        var messages = assembly
            .GetAsyncVoidMethods()
            .Select(method =>
                $"'{method.DeclaringType.Name}.{method.Name}' is an async void method.")
            .ToList();
        Assert.That(messages.Any(),
            Is.False,
            "Async void methods found!" + Environment.NewLine + string.Join(Environment.NewLine, messages));
    }
}

public static class ReflectionUtils
{
    private static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t is not null);
        }
    }

    public static IEnumerable<MethodInfo> GetAsyncVoidMethods(this Assembly assembly)
    {
        return assembly.GetLoadableTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.NonPublic
                | BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            .Where(method => method.HasAttribute<AsyncStateMachineAttribute>())
            .Where(method => method.ReturnType == typeof(void));
    }

    private static bool HasAttribute<TAttribute>(this MethodInfo method) where TAttribute : Attribute
    {
        return method.GetCustomAttributes(typeof(TAttribute), false).Any();
    }
}