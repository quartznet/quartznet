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

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

using Quartz.Impl;

namespace Quartz.Impl;

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
    private static readonly ConcurrentDictionary<Type, Type> createdTypes = new();
    private static int typeNameSuffix;

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
        // Keyed on the Type, not its name: two assemblies can declare the same namespace-qualified
        // interface, and handing back the other one's proxy fails as an InvalidCastException naming two
        // types that read identically.
        var result = createdTypes.GetOrAdd(interfaceType, static t => DoCreate(t));
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
        // The cache is keyed on the Type, so two assemblies declaring the same namespace-qualified
        // interface both get here; the emitted name has to distinguish them too, or the second one
        // fails inside DefineType instead of the dictionary.
        var suffix = Interlocked.Increment(ref typeNameSuffix).ToString(CultureInfo.InvariantCulture);
        var typeName = interfaceType.Namespace is not null ?
            $"{AssemblyName}.{interfaceType.Namespace}.{interfaceType.Name}Instance{suffix}" :
            $"{AssemblyName}.{interfaceType.Name}Instance{suffix}";

        try
        {
            var parentType = typeof(DelegatingScheduler);

            var typeBuilder = moduleBuilder.DefineType(
                name: typeName,
                attr: TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
                parent: parentType,
                interfaces: [interfaceType]
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