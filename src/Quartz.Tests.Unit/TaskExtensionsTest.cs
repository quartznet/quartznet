using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class TaskExtensionsTest
    {
        [Test]
        public void IsCompletedSuccessfully_Created()
        {
            var task = new Task(() => Task.Delay(300));

            Assert.False(task.IsCompletedSuccessfully());
            Assert.AreEqual(TaskStatus.Created, task.Status);
        }

        [Test]
        public void IsCompletedSuccessfully_WaitingToRun()
        {
            var task = new Task(() => Thread.Sleep(300));
            task.Start();

            Assert.False(task.IsCompletedSuccessfully());
            Assert.AreEqual(TaskStatus.WaitingToRun, task.Status);
        }

        [Test]
        public void IsCompletedSuccessfully_Running()
        {
            var task = new Task(() => Thread.Sleep(300));
            task.Start();

            task.Wait(50);

            Assert.False(task.IsCompletedSuccessfully());
            Assert.AreEqual(TaskStatus.Running, task.Status);
        }

        [Test]
        public void IsCompletedSuccessfully_WaitingForActivation()
        {
            var task = Task.Run(() => Task.Delay(300));

            Assert.False(task.IsCompletedSuccessfully());
            Assert.AreEqual(TaskStatus.WaitingForActivation, task.Status);
        }

        [Test]
        public void IsCompletedSuccessfully_Canceled()
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;

            var task = Task.Run(() =>
                {

                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                }, ct);

            tokenSource.Cancel();

            try
            {
                task.GetAwaiter().GetResult();
                Assert.Fail();
            }
            catch (OperationCanceledException)
            {
                Assert.False(task.IsCompletedSuccessfully());
                Assert.AreEqual(TaskStatus.Canceled, task.Status);
            }
        }

        [Test]
        public void IsCompletedSuccessfully_Faulted()
        {
            var task = Task.Run(() => throw new ApplicationException());

            try
            {
                task.GetAwaiter().GetResult();
                Assert.Fail();
            }
            catch (ApplicationException)
            {
                Assert.False(task.IsCompletedSuccessfully());
                Assert.AreEqual(TaskStatus.Faulted, task.Status);
            }
        }

        [Test]
        public void IsCompletedSuccessfully_RanToCompletion()
        {
            var task = Task.Run(() => { });
            task.Wait(30);

            Assert.True(task.IsCompletedSuccessfully());
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
        }
    }
}
