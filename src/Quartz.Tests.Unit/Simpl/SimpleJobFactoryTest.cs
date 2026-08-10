using Quartz.Jobs;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Simpl;

public class SimpleJobFactoryTest
{
    private SimpleJobFactory factory;

    [SetUp]
    public void SetUp()
    {
        factory = new SimpleJobFactory();
    }

    [Test]
    public async Task ShouldDisposeDisposableJobs()
    {
        var disposableJob = new DisposableJob();
        await factory.ReturnJob(new JobScope(disposableJob));
        disposableJob.WasDisposed.Should().BeTrue("job was not disposed");
    }

    [Test]
    public async Task ShouldDisposeAsyncDisposableJobs()
    {
        var disposableJob = new AsyncDisposableJob();
        await factory.ReturnJob(new JobScope(disposableJob));
        disposableJob.WasDisposed.Should().BeTrue("job was not disposed");
    }

    [Test]
    public async Task ShouldDisposeScopeState()
    {
        var disposableJob = new DisposableJob();
        var state = new DisposableState();

        await factory.ReturnJob(new JobScope(disposableJob, state));

        disposableJob.WasDisposed.Should().BeTrue("job was not disposed");
        state.WasDisposed.Should().BeTrue("factory state was not disposed");
    }

    [Test]
    public async Task ShouldLeaveStateAloneWhenItIsNotDisposable()
    {
        // The point of JobScope.State is that a factory can put anything there; only the disposable
        // case gets any help from the base factory.
        var disposableJob = new DisposableJob();
        var state = new NotDisposableState();

        var act = async () => await factory.ReturnJob(new JobScope(disposableJob, state));

        await act.Should().NotThrowAsync();
        disposableJob.WasDisposed.Should().BeTrue("the job is still disposed even when the state alongside it is not");
        state.WasTornDown.Should().BeFalse("state the factory cannot dispose through an interface must be left untouched");
    }

    [Test]
    public async Task ShouldProduceJobInstanceFromBundle()
    {
        var scope = await factory.CreateJob(TestUtil.NewMinimalTriggerFiredBundle(), null!);

        scope.Job.Should().BeOfType<NoOpJob>();
        scope.State.Should().BeNull("the simple factory has no per-fire state to carry");
    }

    public class DisposableJob : IJob, IDisposable
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public void Dispose()
        {
            WasDisposed = true;
        }

        public bool WasDisposed { get; private set; }
    }

    private sealed class AsyncDisposableJob : IJob, IAsyncDisposable
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return default;
        }

        public bool WasDisposed { get; private set; }
    }

    private sealed class DisposableState : IDisposable
    {
        public void Dispose()
        {
            WasDisposed = true;
        }

        public bool WasDisposed { get; private set; }
    }

    /// <summary>
    /// Deliberately implements neither <see cref="IDisposable" /> nor <see cref="IAsyncDisposable" />,
    /// while still offering the shape a teardown would reach for. Nothing may call it.
    /// </summary>
    private sealed class NotDisposableState
    {
        public void Dispose()
        {
            WasTornDown = true;
        }

        public bool WasTornDown { get; private set; }
    }
}
