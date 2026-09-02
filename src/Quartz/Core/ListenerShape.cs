using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Quartz.Core;

/// <summary>
/// Refuses a listener that carries a public method with a notification's name but not its signature.
/// </summary>
/// <remarks>
/// <para>
/// Every member of <see cref="IJobListener" />, <see cref="ITriggerListener" /> and
/// <see cref="ISchedulerListener" /> has a default implementation, which is what lets a listener write
/// only the notifications it cares about. The price is that a member with the wrong signature is not a
/// compile error: it simply stops being an implementation of anything. The default body runs instead,
/// and the method becomes dead code the scheduler never calls. Three migrations produce exactly that
/// shape — a 3.x listener whose members return <see cref="Task" />, a listener written against an
/// earlier 4.0 preview whose callbacks do not take the scheduler first, and one whose
/// <c>TriggerMisfired</c> still leads with the scheduler rather than the trigger — and nothing in the
/// build names the dead member.
/// </para>
/// <para>
/// So the shape is checked once, where the listener is registered, and a method whose name matches a
/// notification but which does not implement it is refused by name and by signature.
/// </para>
/// <para>
/// A method that deliberately overloads a notification's name for an unrelated purpose is refused too.
/// That is accepted: it is a rare thing to write, a rename settles it, and the alternative is letting
/// the far commoner stale signature through in silence. Explicit interface implementations are not
/// public methods of the class, so a listener that implements the interface explicitly is never
/// examined and never refused.
/// </para>
/// </remarks>
internal static class ListenerShape
{
    private const string MigrationGuide = "https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html";

    /// <summary>
    /// What <see cref="Type.GetInterfaceMap" /> asks of the interface it is given. Only the three listener
    /// interfaces are ever passed, each as a <c>typeof</c> of a type this assembly declares, so nothing has
    /// to be kept that would not be kept anyway.
    /// </summary>
    private const DynamicallyAccessedMemberTypes ListenerInterfaceMembers =
        DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods;

    /// <summary>
    /// The pairs already found sound. A listener registered through the container is checked where it is
    /// registered and again when it reaches the listener manager, and a host with many schedulers
    /// registers the same listener type once per scheduler; the shape of a type does not change between
    /// those moments, so it is worked out once.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type ListenerType, Type ListenerInterface), bool> verified = new();

    /// <summary>
    /// Refuses <paramref name="listenerType" /> if it shadows a member of <paramref name="listenerInterface" />.
    /// </summary>
    /// <param name="listenerType">
    /// The listener's type. For a registration made by type or by instance this is the type the
    /// application named, which is the one whose methods it wrote.
    /// </param>
    /// <param name="listenerInterface">
    /// <see cref="IJobListener" />, <see cref="ITriggerListener" /> or <see cref="ISchedulerListener" />.
    /// </param>
    /// <exception cref="SchedulerConfigException">
    /// A public instance method of <paramref name="listenerType" /> has an interface member's name but
    /// does not implement it.
    /// </exception>
    public static void Verify(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type listenerType,
        [DynamicallyAccessedMembers(ListenerInterfaceMembers)] Type listenerInterface)
    {
        // A registration naming an interface or a type that is not a listener of this kind - which
        // AddJobListener<IJobListener>(factory) is - describes no methods to check. What the factory
        // actually produces is checked when it reaches the listener manager.
        if (listenerType.IsInterface || !listenerInterface.IsAssignableFrom(listenerType))
        {
            return;
        }

        if (verified.ContainsKey((listenerType, listenerInterface)))
        {
            return;
        }

        Check(listenerType, listenerInterface);
        verified[(listenerType, listenerInterface)] = true;
    }

    private static void Check(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type listenerType,
        [DynamicallyAccessedMembers(ListenerInterfaceMembers)] Type listenerInterface)
    {
        InterfaceMapping mapping = listenerType.GetInterfaceMap(listenerInterface);

        // The methods that really do implement the interface. A notification the listener leaves to its
        // default maps to the interface's own method, which is never a member of the listener class, so
        // nothing the loop below looks at can match it by accident.
        HashSet<MethodInfo> implementing = new(mapping.TargetMethods);

        foreach (MethodInfo method in listenerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (implementing.Contains(method))
            {
                continue;
            }

            MethodInfo? shadowed = Array.Find(
                mapping.InterfaceMethods,
                candidate => string.Equals(candidate.Name, method.Name, StringComparison.Ordinal));

            if (shadowed is null)
            {
                continue;
            }

            Throw.SchedulerConfigException(Describe(listenerType, listenerInterface, method, shadowed));
        }
    }

    private static string Describe(Type listenerType, Type listenerInterface, MethodInfo method, MethodInfo shadowed)
    {
        StringBuilder message = new();
        message.Append(listenerType)
            .Append(" declares '")
            .Append(Signature(method))
            .Append("', which does not implement ")
            .Append(listenerInterface.Name)
            .Append('.')
            .Append(shadowed.Name)
            .Append(". The interface member is '")
            .Append(Signature(shadowed))
            .Append("': the names match but the signatures do not, and every member of ")
            .Append(listenerInterface.Name)
            .Append(" has a default implementation, so this compiles and the default runs instead. The scheduler never calls ")
            .Append(method.Name)
            .Append(". ");

        string? section = null;

        if (ReturnsTask(method))
        {
            message.Append("Listener members return ValueTask rather than Task since 4.0. ");
            section = "#tasks-changed-to-valuetask";
        }

        if (MissesTheScheduler(method, shadowed))
        {
            message.Append("Listener callbacks take IScheduler scheduler first in 4.0. ");
            section ??= "#listeners-are-told-which-scheduler-is-calling";
        }

        if (ReordersTheParameters(method, shadowed))
        {
            message.Append("The parameters are the notification's own, in a different order. ");

            if (listenerInterface == typeof(ITriggerListener)
                && string.Equals(shadowed.Name, nameof(ITriggerListener.TriggerMisfired), StringComparison.Ordinal))
            {
                message.Append("TriggerMisfired takes the trigger first in 4.0. ");
                section ??= "#triggermisfired-takes-the-trigger-first";
            }
        }

        message.Append("Correct the signature, or rename the method if it is not meant to be that notification. See ")
            .Append(MigrationGuide)
            .Append(section)
            .Append('.');

        return message.ToString();
    }

    /// <summary>
    /// Whether the method has the return type a 3.x listener had.
    /// </summary>
    private static bool ReturnsTask(MethodInfo method)
    {
        Type returnType = method.ReturnType;
        return returnType == typeof(Task)
            || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>));
    }

    /// <summary>
    /// Whether the notification leads with the scheduler and the method does not, which is the shape a
    /// listener written against an earlier 4.0 preview has.
    /// </summary>
    private static bool MissesTheScheduler(MethodInfo method, MethodInfo shadowed)
    {
        ParameterInfo[] expected = shadowed.GetParameters();
        if (expected.Length == 0 || expected[0].ParameterType != typeof(IScheduler))
        {
            return false;
        }

        ParameterInfo[] actual = method.GetParameters();
        return actual.Length == 0 || actual[0].ParameterType != typeof(IScheduler);
    }

    /// <summary>
    /// Whether the method takes exactly the notification's parameters but in another order, which is the
    /// shape a listener has whose <c>TriggerMisfired</c> still leads with the scheduler.
    /// </summary>
    /// <remarks>
    /// Said separately from the signature pair in the message because the two signatures are otherwise
    /// easy to read as the same one: only the order differs, and a reader comparing them left to right
    /// finds every type they expected.
    /// </remarks>
    private static bool ReordersTheParameters(MethodInfo method, MethodInfo shadowed)
    {
        ParameterInfo[] expected = shadowed.GetParameters();
        ParameterInfo[] actual = method.GetParameters();

        if (expected.Length != actual.Length)
        {
            return false;
        }

        List<Type> unaccounted = new(actual.Length);
        bool sameOrder = true;

        for (int i = 0; i < actual.Length; i++)
        {
            unaccounted.Add(actual[i].ParameterType);
            sameOrder &= actual[i].ParameterType == expected[i].ParameterType;
        }

        if (sameOrder)
        {
            // Something other than the order is wrong - the return type, most likely - and saying the
            // parameters were reordered when they were not would send the reader looking for nothing.
            return false;
        }

        foreach (ParameterInfo parameter in expected)
        {
            if (!unaccounted.Remove(parameter.ParameterType))
            {
                return false;
            }
        }

        return true;
    }

    private static string Signature(MethodInfo method)
    {
        StringBuilder signature = new();
        signature.Append(TypeName(method.ReturnType)).Append(' ').Append(method.Name).Append('(');

        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                signature.Append(", ");
            }

            signature.Append(TypeName(parameters[i].ParameterType));
        }

        return signature.Append(')').ToString();
    }

    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int arity = name.IndexOf('`', StringComparison.Ordinal);

        StringBuilder formatted = new();
        formatted.Append(arity < 0 ? name : name[..arity]).Append('<');

        Type[] arguments = type.GetGenericArguments();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (i > 0)
            {
                formatted.Append(", ");
            }

            formatted.Append(TypeName(arguments[i]));
        }

        return formatted.Append('>').ToString();
    }
}
