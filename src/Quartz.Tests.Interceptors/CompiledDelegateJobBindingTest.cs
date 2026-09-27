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

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Interceptors;

/// <summary>
/// <c>DelegateJobBindingTest</c>'s expectations, for handlers the source generator bound.
/// </summary>
/// <remarks>
/// <para>
/// That test binds each handler by calling <see cref="DelegateJobBinding.Bind" />, which no interceptor
/// can reach. Here each handler is written at an <c>AddJob</c> call, which this project's build
/// intercepts, and the binding is read back out of the registry the call wrote it to — so what is run is
/// the code the generator wrote, and every test first says that it is.
/// </para>
/// <para>
/// The refusals are not repeated. A handler Quartz refuses is one the generator leaves alone, so it is
/// refused by the same code on both paths; <c>DelegateJobsGeneratorTest</c> is where that is held.
/// </para>
/// </remarks>
public sealed class CompiledDelegateJobBindingTest
{
    private readonly IJobExecutionContext context = A.Fake<IJobExecutionContext>();

    [Test]
    public async Task TheContextParameterIsTheFiring()
    {
        IJobExecutionContext? seen = null;

        DelegateJobBinding binding = Added(q => q.AddJob("job", (IJobExecutionContext firing) => { seen = firing; }));
        await binding.Invoke(context, EmptyServices(), CancellationToken.None);

        seen.Should().BeSameAs(context, "IJobExecutionContext is the firing, not a service to resolve");
    }

    [Test]
    public async Task TheTokenParameterIsTheFiringsToken()
    {
        using CancellationTokenSource source = new();
        CancellationToken seen = default;

        DelegateJobBinding binding = Added(q => q.AddJob("job", (CancellationToken token) => { seen = token; }));
        await binding.Invoke(context, EmptyServices(), source.Token);

        seen.Should().Be(source.Token, "the handler is handed the token the firing was run with");
    }

    [Test]
    public async Task TheServiceProviderParameterIsTheFiringsScope()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        IServiceProvider? seen = null;

        DelegateJobBinding binding = Added(q => q.AddJob("job", (IServiceProvider provider) => { seen = provider; }));
        await binding.Invoke(context, services, CancellationToken.None);

        seen.Should().BeSameAs(services, "IServiceProvider is the scope the firing resolves from, as it is handed");
    }

    [Test]
    public async Task AnyOtherParameterIsARequiredServiceFromTheScope()
    {
        Clock clock = new();
        await using ServiceProvider services = new ServiceCollection().AddSingleton(clock).BuildServiceProvider();
        Clock? seen = null;

        DelegateJobBinding binding = Added(q => q.AddJob("job", (Clock resolved, IJobExecutionContext _, CancellationToken _) => { seen = resolved; }));
        await binding.Invoke(context, services, CancellationToken.None);

        seen.Should().BeSameAs(clock, "a parameter of any other type is resolved from the firing's scope");
    }

    [Test]
    public async Task AServiceTheScopeCannotGiveFailsTheFiringWithTheContainersOwnMessage()
    {
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        DelegateJobBinding compiled = Added(q => q.AddJob("job", (Clock _) => { }));
        DelegateJobBinding reflected = Reflected((Clock _) => { });

        Action compiledFiring = () => compiled.Invoke(context, services, CancellationToken.None);
        Action reflectedFiring = () => reflected.Invoke(context, services, CancellationToken.None);

        string expected = reflectedFiring.Should().Throw<InvalidOperationException>().Which.Message;
        compiledFiring.Should().Throw<InvalidOperationException>(
            "a service parameter is required, and the firing is the first moment its absence is known")
            .Which.Message.Should().Be(expected, "both paths ask the container the same question, so it gives the same answer");
    }

    [Test]
    public async Task ATaskIsAwaited()
    {
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask running = Added(q => q.AddJob("job", () => finish.Task)).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompleted.Should().BeFalse("the job is still running until the task it returned has finished");
        finish.SetResult();
        await running;
    }

    [Test]
    public async Task AValueTaskIsAwaited()
    {
        TaskCompletionSource finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ValueTask running = Added(q => q.AddJob("job", () => new ValueTask(finish.Task))).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompleted.Should().BeFalse("the job is still running until the value task it returned has finished");
        finish.SetResult();
        await running;
    }

    [Test]
    public void AHandlerThatReturnsNothingHasFinishedWhenItReturns()
    {
        int runs = 0;

        ValueTask running = Added(q => q.AddJob("job", () => { runs++; })).Invoke(context, EmptyServices(), CancellationToken.None);

        running.IsCompletedSuccessfully.Should().BeTrue("a void handler has nothing left to wait for once it returns");
        runs.Should().Be(1);
    }

    /// <summary>
    /// The generated code cannot call into Quartz's internals, so it carries the sentence itself; this is
    /// what keeps the two copies saying the same thing.
    /// </summary>
    [Test]
    public void ANullTaskIsReportedInQuartzsWords()
    {
        DelegateJobBinding compiled = Added(q => q.AddJob("job", () => (Task) null!));

        Action act = () => compiled.Invoke(context, EmptyServices(), CancellationToken.None);

        act.Should().Throw<InvalidOperationException>("awaiting null would fail with a NullReferenceException that says nothing")
            .Which.Message.Should().Be(DelegateJobBinding.NullTaskMessage,
                "a handler bound by reflection reports a null task in exactly these words");
    }

    [Test]
    public void AnExceptionIsTheHandlersOwnRatherThanWrapped()
    {
        DelegateJobBinding binding = Added(q => q.AddJob("job", () => { throw new TimeoutException("the handler's own"); }));

        Action act = () => binding.Invoke(context, EmptyServices(), CancellationToken.None);

        act.Should().Throw<TimeoutException>(
            "listeners, the retry policy and the log should see what the handler threw, not a TargetInvocationException around it")
            .WithMessage("the handler's own");
    }

    [Test]
    public async Task AStaticMethodGroupIsBound()
    {
        StaticHandlers.Runs = 0;

        await Added(q => q.AddJob("job", StaticHandlers.Run)).Invoke(context, EmptyServices(), CancellationToken.None);

        StaticHandlers.Runs.Should().Be(1, "a static method has no instance to be invoked on, and needs none");
    }

    [Test]
    public async Task AnExtensionMethodGroupIsCalledWithItsReceiver()
    {
        Counter counter = new();

        await Added(q => q.AddJob("job", counter.Increment)).Invoke(context, EmptyServices(), CancellationToken.None);

        counter.Value.Should().Be(1,
            "a delegate over an extension method is closed over its receiver, which the delegate, not the scope, supplies");
    }

    [Test]
    public void OnlyServiceParametersAreListedForTheValidator()
    {
        DelegateJobBinding binding = Added(q => q.AddJob("job", (IJobExecutionContext _, Clock _, CancellationToken _, IServiceProvider _) => { }));

        binding.ServiceParameters.Select(parameter => parameter.ParameterType).Should().Equal([typeof(Clock)],
            "the validator reads the handler the application wrote, not the binding generated for it");
    }

    /// <summary>
    /// Adds the one delegate job a registration writes and hands back its binding, having said that it
    /// is the generated one.
    /// </summary>
    private static DelegateJobBinding Added(Action<IQuartzBuilder> register)
    {
        ServiceCollection services = new();
        services.AddQuartz(register);

        DelegateJobBinding binding = DelegateJobRegistry.For(services).Declared(Options.DefaultName)
            .Should().ContainSingle("the registration adds one delegate job").Subject.Binding;

        binding.IsCompiled.Should().BeTrue(
            "the handler is a lambda or a method group written at an AddJob call this project's build intercepts; "
            + "a reflective binding here means the generator stopped binding it");

        return binding;
    }

    private static DelegateJobBinding Reflected(Delegate handler)
    {
        DelegateJobBinding binding = DelegateJobBinding.Bind(handler, "handler");
        binding.IsCompiled.Should().BeFalse("Bind is called directly, which nothing intercepts");
        return binding;
    }

    private static IServiceProvider EmptyServices() => new ServiceCollection().BuildServiceProvider();

    public sealed class Clock;

    public sealed class Counter
    {
        public int Value { get; set; }
    }
}

internal static class StaticHandlers
{
    public static int Runs { get; set; }

    public static void Run() => Runs++;
}

internal static class CounterExtensions
{
    public static void Increment(this CompiledDelegateJobBindingTest.Counter counter) => counter.Value++;
}
