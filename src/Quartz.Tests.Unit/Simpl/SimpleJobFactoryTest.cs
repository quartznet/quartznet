using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl;

[TestFixture]
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
        await factory.ReturnJob(disposableJob);
        Assert.That(disposableJob.WasDisposed, Is.True, "job was not disposed");
    }

    public class DisposableJob : IJob, IDisposable
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return default;
        }

        public void Dispose()
        {
            WasDisposed = true;
        }

        public bool WasDisposed { get; private set; }
    }
}