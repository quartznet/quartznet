using System;
using System.Threading.Tasks;
using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
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
        public void ShouldDisposeDisposableJobs()
        {
            var disposableJob = new DisposableJob();
            factory.ReturnJob(disposableJob);
            Assert.That(disposableJob.WasDisposed, Is.True, "job was not disposed");
        }

        public class DisposableJob : IJob, IDisposable
        {
            private bool wasDisposed;

            public Task Execute(IJobExecutionContext context)
            {
                return Task.FromResult(0);
            }

            public void Dispose()
            {
                wasDisposed = true;
            }

            public bool WasDisposed
            {
                get { return wasDisposed; }
            }
        }
    }
}