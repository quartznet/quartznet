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

using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;

namespace Quartz.Impl;

/// <summary>
/// A delegate job's handler, with each of its parameters bound to where its argument comes from.
/// </summary>
/// <remarks>
/// <para>
/// Bound once, when the job is added: the parameters are read off the delegate's method and each is
/// given its source, so a firing does nothing reflective beyond filling the argument array and one
/// <see cref="MethodBase.Invoke(object?, BindingFlags, Binder?, object?[], System.Globalization.CultureInfo?)" />.
/// A handler that could never run — a result with nowhere to go, an argument passed by reference — is
/// refused there, where it was written, rather than on its first firing.
/// </para>
/// <para>
/// Nothing here builds code at run time: no expression tree, no emitted IL, no generic closed over a
/// type only known at run time. That is what keeps the registration members free of
/// <c>RequiresDynamicCode</c>. A source-generated binding that needs no reflection at all is #3882.
/// </para>
/// </remarks>
internal sealed class DelegateJobBinding
{
    private readonly MethodInfo method;

    /// <summary>
    /// What the method is invoked on: the closure or declaring instance, or <see langword="null" /> for
    /// a static method.
    /// </summary>
    private readonly object? instance;

    /// <summary>
    /// The first argument of a static method the delegate was closed over — an extension method group
    /// such as <c>cache.Evict</c> — which the delegate supplies itself and the method still declares.
    /// </summary>
    private readonly object? boundFirstArgument;

    private readonly bool closedOverFirstArgument;
    private readonly BoundParameter[] parameters;
    private readonly Completion completion;

    private DelegateJobBinding(
        MethodInfo method,
        object? instance,
        object? boundFirstArgument,
        bool closedOverFirstArgument,
        BoundParameter[] parameters,
        Completion completion)
    {
        this.method = method;
        this.instance = instance;
        this.boundFirstArgument = boundFirstArgument;
        this.closedOverFirstArgument = closedOverFirstArgument;
        this.parameters = parameters;
        this.completion = completion;
    }

    /// <summary>
    /// The parameters the handler is given services for, in declaration order, which is what
    /// <see cref="RegisteredJobConstructorValidator" /> reads to refuse a scheduler's own parts.
    /// </summary>
    public List<ParameterInfo> ServiceParameters
    {
        get
        {
            List<ParameterInfo> services = [];
            foreach (BoundParameter parameter in parameters)
            {
                if (parameter.Source == ArgumentSource.Service)
                {
                    services.Add(parameter.Parameter);
                }
            }

            return services;
        }
    }

    /// <summary>
    /// Binds a handler, refusing one that could not be run.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="parameterName">The name the caller knows the handler by, for the exceptions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The handler's shape is one a job cannot be run as.</exception>
    public static DelegateJobBinding Bind(Delegate handler, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(handler, parameterName);

        if (!handler.HasSingleTarget)
        {
            Throw.ArgumentException(
                "A delegate job is one handler, and this delegate combines several: the job would run only the "
                + "last of them. Register each as a job of its own.",
                parameterName);
        }

        MethodInfo method = handler.Method;
        object? target = handler.Target;

        // An instance method with nothing to call it on is an open instance delegate, whose first
        // argument would have to be the instance - and nothing here could supply one.
        if (!method.IsStatic && target is null)
        {
            Throw.ArgumentException(
                $"The handler is an open instance delegate over {method.Name}, which has no instance to be "
                + "called on. Pass a delegate bound to its instance, or a lambda.",
                parameterName);
        }

        Completion completion = CompletionOf(method, parameterName);

        // A static method with a target is closed over its first argument: the delegate supplies it, but
        // the method still declares it, and it has to be passed to Invoke in that position.
        bool closedOverFirstArgument = method.IsStatic && target is not null;
        ParameterInfo[] declared = method.GetParameters();
        int first = closedOverFirstArgument ? 1 : 0;

        BoundParameter[] parameters = new BoundParameter[declared.Length - first];
        for (int i = first; i < declared.Length; i++)
        {
            parameters[i - first] = BindParameter(declared[i], parameterName);
        }

        return new DelegateJobBinding(
            method,
            closedOverFirstArgument ? null : target,
            closedOverFirstArgument ? target : null,
            closedOverFirstArgument,
            parameters,
            completion);
    }

    /// <summary>
    /// Runs the handler for one firing.
    /// </summary>
    /// <param name="context">The firing.</param>
    /// <param name="services">The firing's scope, which every service argument is resolved from.</param>
    /// <param name="cancellationToken">The firing's token.</param>
    /// <exception cref="InvalidOperationException">
    /// A service the handler asks for is not registered, or a handler declared to return a
    /// <see cref="Task" /> returned <see langword="null" />.
    /// </exception>
    public ValueTask Invoke(IJobExecutionContext context, IServiceProvider services, CancellationToken cancellationToken)
    {
        int offset = closedOverFirstArgument ? 1 : 0;
        object?[] arguments = new object?[parameters.Length + offset];

        if (closedOverFirstArgument)
        {
            arguments[0] = boundFirstArgument;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            BoundParameter parameter = parameters[i];
            arguments[i + offset] = parameter.Source switch
            {
                ArgumentSource.Context => context,
                ArgumentSource.CancellationToken => cancellationToken,
                ArgumentSource.Services => services,
                _ => services.GetRequiredService(parameter.Parameter.ParameterType),
            };
        }

        // DoNotWrapExceptions, so a handler that throws is reported with its own exception rather than a
        // TargetInvocationException around it - which is what the listeners, the retry policy and the log
        // would otherwise all have seen.
        object? result = method.Invoke(instance, BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null);

        switch (completion)
        {
            case Completion.Task:
                if (result is not Task task)
                {
                    Throw.InvalidOperationException(
                        "The delegate job's handler returned null rather than a Task, so there is nothing to await. "
                        + "Return a completed task instead, or make the handler async.");
                    return default;
                }

                return new ValueTask(task);
            case Completion.ValueTask:
                return (ValueTask) result!;
            default:
                return default;
        }
    }

    /// <summary>
    /// How a handler says it has finished, refusing a return type the scheduler could not wait on or
    /// would have to throw away.
    /// </summary>
    private static Completion CompletionOf(MethodInfo method, string parameterName)
    {
        Type returnType = method.ReturnType;

        if (returnType == typeof(ValueTask))
        {
            return Completion.ValueTask;
        }

        if (returnType == typeof(Task))
        {
            return Completion.Task;
        }

        if (returnType == typeof(void))
        {
            // An async void method returns before it has finished and reports its failure to nobody, so
            // the job would be recorded complete - and its exception lost - while it was still running.
            if (method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false))
            {
                Throw.ArgumentException(
                    "The handler is async void, which cannot be awaited: the job would be reported complete, and "
                    + "any exception it throws lost, before it had finished. Return Task instead.",
                    parameterName);
            }

            return Completion.Void;
        }

        Throw.ArgumentException(
            $"A delegate job's handler returns Task, ValueTask or nothing, and this one returns "
            + $"{RegisteredJobConstructorValidator.TypeName(returnType)}. A job has no result for the scheduler "
            + "to take: set IJobExecutionContext.Result if something downstream reads one.",
            parameterName);
        return default;
    }

    private static BoundParameter BindParameter(ParameterInfo parameter, string parameterName)
    {
        Type type = parameter.ParameterType;

        // Each argument is supplied as an object and passed through Invoke, which none of these survive:
        // a ref, out or in parameter is a location rather than a value, a pointer is not an object, and a
        // ref struct cannot be boxed.
        if (type.IsByRef || type.IsPointer || type.IsByRefLike)
        {
            Throw.ArgumentException(
                $"The handler's parameter '{parameter.Name}' cannot be supplied: a delegate job's arguments are "
                + "values the scheduler resolves and passes as objects, which a ref, out, in, pointer or ref struct "
                + "parameter cannot be.",
                parameterName);
        }

        ArgumentSource source = type == typeof(IJobExecutionContext) ? ArgumentSource.Context
            : type == typeof(CancellationToken) ? ArgumentSource.CancellationToken
            : type == typeof(IServiceProvider) ? ArgumentSource.Services
            : ArgumentSource.Service;

        return new BoundParameter(source, parameter);
    }

    /// <summary>
    /// Where one argument comes from.
    /// </summary>
    private enum ArgumentSource
    {
        /// <summary>The firing's <see cref="IJobExecutionContext" />.</summary>
        Context,

        /// <summary>The firing's token, the same one <see cref="IJobExecutionContext.CancellationToken" /> carries.</summary>
        CancellationToken,

        /// <summary>The firing's scope itself.</summary>
        Services,

        /// <summary>A required service, resolved from the firing's scope.</summary>
        Service,
    }

    /// <summary>
    /// How the handler says it has finished.
    /// </summary>
    private enum Completion
    {
        Void,
        Task,
        ValueTask,
    }

    private readonly record struct BoundParameter(ArgumentSource Source, ParameterInfo Parameter);
}
