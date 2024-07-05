using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

using Quartz.Impl;

namespace Quartz.Simpl;

/// <summary>
/// Creates dynamic IScheduler types with custom marker interface.
/// </summary>
/// <remarks>
/// This implementation is based on BusInstanceBuilder from MassTransit:
/// https://github.com/MassTransit/MassTransit/blob/master/src/MassTransit/DependencyInjection/DependencyInjection/BusInstanceBuilder.cs
/// </remarks>
internal static class SchedulerTypeBuilder
{
    private const string AssemblyName = "Quartz.SchedulerInstances";

    private static readonly ModuleBuilder moduleBuilder = CreateModuleBuilder();
    private static readonly ConcurrentDictionary<string, Type> createdTypes = new();

    private static ModuleBuilder CreateModuleBuilder()
    {
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), AssemblyBuilderAccess.RunAndCollect);
        var builder = assemblyBuilder.DefineDynamicModule(AssemblyName);

        return builder;
    }

    public static Type Create<TScheduler>() where TScheduler : class, IScheduler
    {
        return Create(typeof(TScheduler));
    }

    public static Type Create(Type interfaceType)
    {
        var result = createdTypes.GetOrAdd(interfaceType.FullName ?? "", _ => DoCreate(interfaceType));
        return result;

        static Type DoCreate(Type interfaceType)
        {
            AssertInterfaceType(interfaceType);
            var schedulerType = CreateTypeForInterface(interfaceType);
            return schedulerType;
        }
    }

    private static void AssertInterfaceType(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} is not interface", nameof(interfaceType));
        }

        if (!interfaceType.IsPublic)
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} is not public", nameof(interfaceType));
        }

        if (interfaceType.IsGenericType)
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} is generic", nameof(interfaceType));
        }

        if (!typeof(IScheduler).IsAssignableFrom(interfaceType))
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} does not implement IScheduler", nameof(interfaceType));
        }

        if (interfaceType.IsNested)
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} is nested type", nameof(interfaceType));
        }

        if (interfaceType.GetInterfaces().Any(x => x != typeof(IScheduler)))
        {
            throw new ArgumentException($"Scheduler type {interfaceType.FullName} implements other interfaces than {nameof(IScheduler)}", nameof(interfaceType));
        }
    }

    private static Type CreateTypeForInterface(Type interfaceType)
    {
        var typeName = interfaceType.Namespace is not null ?
            $"{AssemblyName}.{interfaceType.Namespace}.{interfaceType.Name}Instance" :
            $"{AssemblyName}.{interfaceType.Name}Instance";

        try
        {
            var parentType = typeof(DelegatingScheduler);

            var typeBuilder = moduleBuilder.DefineType(
                name: typeName,
                attr: TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
                parent: parentType,
                interfaces: new[] { interfaceType }
            );

            var parameterTypes = new[] { typeof(IScheduler) };

            var ctorParent = parentType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null)!;
            var ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameterTypes);

            var il = ctorBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, ctorParent);
            il.Emit(OpCodes.Ret);

            return typeBuilder.CreateTypeInfo()!.AsType();
        }
        catch (Exception ex)
        {
            var message = $"Exception creating scheduler instance ({typeName}) for {interfaceType.FullName}";
            throw new InvalidOperationException(message, ex);
        }
    }
}