using NUnit.Framework;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl;

public class TaskSchedulingThreadPoolTest
{
    [Test]
    public void MaxConcurrencyIsRespected()
    {
        var threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);
        threadPool.Initialize();

        var logBook = new List<string>();
        var task1Done = new ManualResetEvent(false);
        var task2Done = new ManualResetEvent(false);

        threadPool.RunInThread(async () =>
        {
            lock (logBook)
            {
                logBook.Add("START #1");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            lock (logBook)
            {
                logBook.Add("END #1");
            }

            task1Done.Set();
        });

        threadPool.RunInThread(async () =>
        {
            lock (logBook)
            {
                logBook.Add("START #2");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            lock (logBook)
            {
                logBook.Add("END #2");
            }

            task2Done.Set();
        });

        Assert.IsTrue(WaitHandle.WaitAll(new[] { task1Done, task2Done }, TimeSpan.FromSeconds(1)));

        Assert.AreEqual(4, logBook.Count);
        Assert.AreEqual("START #1", logBook[0]);
        Assert.AreEqual("END #1", logBook[1]);
        Assert.AreEqual("START #2", logBook[2]);
        Assert.AreEqual("END #2", logBook[3]);
    }

    private class CustomTaskSchedulingThreadPool : TaskSchedulingThreadPool
    {
        private readonly TaskScheduler taskScheduler;

        public CustomTaskSchedulingThreadPool(TaskScheduler taskScheduler, int maximumConcurrency)
            : base(maximumConcurrency)
        {
            this.taskScheduler = taskScheduler;
        }

        protected override TaskScheduler GetDefaultScheduler()
        {
            return taskScheduler;
        }
    }
}