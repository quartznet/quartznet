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

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What each of a delegate job's parameters is handed, which handlers are refused where they are
/// registered, and how a handler says it has finished.
/// </summary>
public sealed class DelegateJobBindingTest
{
    private readonly IJobExecutionContext context = A.Fake<IJobExecutionContext>();

    [Test]
    public async Task TheContextParameterIsTheFiring()
    {
        IJobExecutionContext? seen = null;

        await Invoke((IJobExecutionContext firing) => { seen = firing; });

        seen.Should().BeSameAs(context, "IJobExecutionContext is the firing, not a service to resolve");
    }

    [Test]
    public async Task TheTokenParameterIsTheFiringsToken()
    {
        using CancellationTokenSource source = new();
        CancellationToken seen = default;

        await Invoke((CancellationToken token) => { seen = token; }, cancellationToken: source.Token);

        seen.Should().Be(source.Token, "the handler is handed the token the firing was run with");
    }

    [Test]
    public async Task TheServiceProviderParameterIsTheFiringsScope()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        IServiceProvider? seen = null;

        await Invoke((IServiceProvider provider) => { seen = provider; }, services);

        seen.Should().BeSameAs(services, "IServiceProvider is the scope the firing resolves from, as it is handed");
    }

    [Test]
    public async Task AnyOtherParameterIsARequiredServiceFromTheScope()
    {
        Clock clock = new();
        await using ServiceProvider services = new ServiceCollection().AddSingleton(clock).BuildServiceProvider();
        Clock? seen = null;

        await Invoke((Clock resolved, IJobExecutionContext _, CancellationToken _) => { seen = resolved; }, services);

        seen.Should().BeSameAs(clock, "a parameter of any other type is resolved from the firing's scope");
    }

    [Test]
    public async Task AServiceTheScopeCannotGiveFailsTheFiring()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        Func<Task> act = async () => await Invoke((Clock _) => { }, services);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a service parameter is required, and the firing is the first moment its absence is known")
            .WithMessage($"*{nameof(Clock)}*");
    }

    [Test]
    public async Task ATaskIsAwaited()
    {
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask running = Bind(() => finish.Task).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompleted.Should().BeFalse("the job is still running until the task it returned has finished");
        finish.SetResult();
        await running;
    }

    [Test]
    public async Task AValueTaskIsAwaited()
    {
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask running = Bind(() => new ValueTask(finish.Task)).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompleted.Should().BeFalse("the job is still running until the value task it returned has finished");
        finish.SetResult();
        await running;
    }

    [Test]
    public void AHandlerThatReturnsNothingHasFinishedWhenItReturns()
    {
        int runs = 0;

        ValueTask running = Bind(() => { runs++; }).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompletedSuccessfully.Should().BeTrue("a void handler has nothing left to wait for once it returns");
        runs.Should().Be(1);
    }

    [Test]
    public void ANullTaskIsReportedRatherThanAwaited()
    {
        Action act = () => Bind(() => (Task) null!).Invoke(context, EmptyServices(), CancellationToken.None);

        act.Should().Throw<InvalidOperationException>("awaiting null would fail with a NullReferenceException that says nothing")
            .WithMessage("*returned null*");
    }

    [Test]
    public void AnExceptionIsTheHandlersOwnRatherThanWrapped()
    {
        Action act = () => Bind(() => { throw new TimeoutException("the handler's own"); }).Invoke(context, EmptyServices(), CancellationToken.None);

        act.Should().Throw<TimeoutException>(
            "listeners, the retry policy and the log should see what the handler threw, not a TargetInvocationException around it")
            .WithMessage("the handler's own");
    }

    [Test]
    public async Task AStaticMethodGroupIsBound()
    {
        StaticHandlers.Runs = 0;

        await Bind(StaticHandlers.Run).Invoke(context, EmptyServices(), CancellationToken.None);

        StaticHandlers.Runs.Should().Be(1, "a static method has no instance to be invoked on, and needs none");
    }

    [Test]
    public async Task AnExtensionMethodGroupIsCalledWithItsReceiver()
    {
        Counter counter = new();

        await Bind(counter.Increment).Invoke(context, EmptyServices(), CancellationToken.None);

        counter.Value.Should().Be(1,
            "a delegate over an extension method is a static method closed over its first argument, which the "
            + "method still declares and the delegate, not the scope, supplies");
    }

    [Test]
    public void OnlyServiceParametersAreListedForTheValidator()
    {
        DelegateJobBinding binding = Bind((IJobExecutionContext _, Clock _, CancellationToken _, IServiceProvider _) => { });

        binding.ServiceParameters.Select(parameter => parameter.ParameterType).Should().Equal([typeof(Clock)],
            "the firing, its token and its scope are supplied by the scheduler, so only the rest are resolved");
    }

    [Test]
    public void ANullHandlerIsRefused()
    {
        Action act = () => DelegateJobBinding.Bind(null!, "handler");

        act.Should().Throw<ArgumentNullException>().WithParameterName("handler");
    }

    [Test]
    public void AResultTaskIsRefused()
    {
        Action act = () => Bind(() => Task.FromResult(42));

        act.Should().Throw<ArgumentException>("a job has no result for the scheduler to take, and dropping one silently hides a mistake")
            .WithParameterName("handler")
            .WithMessage("*Task<Int32>*IJobExecutionContext.Result*");
    }

    [Test]
    public void AResultValueTaskIsRefused()
    {
        Action act = () => Bind(() => new ValueTask<int>(42));

        act.Should().Throw<ArgumentException>().WithMessage("*ValueTask<Int32>*");
    }

    [Test]
    public void AnyOtherReturnTypeIsRefused()
    {
        Action act = () => Bind(() => "done");

        act.Should().Throw<ArgumentException>().WithMessage("*returns String*");
    }

    [Test]
    public void AnAsyncVoidHandlerIsRefused()
    {
        Action act = () => Bind(AsyncVoidMethod());

        act.Should().Throw<ArgumentException>(
            "an async void handler returns before it has finished, so the job would be reported complete while it was running")
            .WithMessage("*async void*");
    }

    [Test]
    public void AReferenceParameterIsRefused()
    {
        Action act = () => Bind((ref int value) => { value++; });

        act.Should().Throw<ArgumentException>("an argument the scheduler supplies is a value, not a location")
            .WithMessage("*'value'*");
    }

    [Test]
    public void ARefStructParameterIsRefused()
    {
        Action act = () => Bind((Span<byte> buffer) => buffer.Clear());

        act.Should().Throw<ArgumentException>("a ref struct cannot be boxed into the argument array")
            .WithMessage("*'buffer'*");
    }

    [Test]
    public void ACombinedDelegateIsRefused()
    {
        Action first = () => { };
        Action second = () => { };

        Action act = () => DelegateJobBinding.Bind(Delegate.Combine(first, second)!, "handler");

        act.Should().Throw<ArgumentException>("invoking the method of a combined delegate would run only the last of them")
            .WithMessage("*combines several*");
    }

    [Test]
    public void AnOpenInstanceDelegateIsRefused()
    {
        Delegate open = Delegate.CreateDelegate(
            typeof(Action<Counter>),
            typeof(Counter).GetMethod(nameof(Counter.IncrementItself))!);

        Action act = () => DelegateJobBinding.Bind(open, "handler");

        act.Should().Throw<ArgumentException>("an open instance delegate has no instance for its method to be called on")
            .WithMessage("*open instance delegate*");
    }

    private ValueTask Invoke(Delegate handler, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        return Bind(handler).Invoke(context, services ?? EmptyServices(), cancellationToken);
    }

    private static DelegateJobBinding Bind(Delegate handler) => DelegateJobBinding.Bind(handler, "handler");

    private static IServiceProvider EmptyServices() => new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// What an <c>async void</c> method is to reflection: a void method carrying
    /// <see cref="AsyncStateMachineAttribute" />. Emitted into an assembly of its own, because
    /// <c>QualityTest</c> refuses a real one anywhere in this assembly.
    /// </summary>
    private static Action AsyncVoidMethod()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AsyncVoidHandlers"), AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule("AsyncVoidHandlers")
            .DefineType("Handlers", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        MethodBuilder method = type.DefineMethod("RunAsync", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AsyncStateMachineAttribute).GetConstructor([typeof(Type)])!,
            [typeof(object)]));
        method.GetILGenerator().Emit(OpCodes.Ret);

        return type.CreateType().GetMethod("RunAsync")!.CreateDelegate<Action>();
    }

    public sealed class Clock;

    public sealed class Counter
    {
        public int Value { get; set; }

        public void IncrementItself() => Value++;
    }
}

internal static class StaticHandlers
{
    public static int Runs { get; set; }

    public static void Run() => Runs++;

}

internal static class CounterExtensions
{
    public static void Increment(this DelegateJobBindingTest.Counter counter) => counter.Value++;
}
