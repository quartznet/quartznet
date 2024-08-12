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

        Assert.Multiple(() =>
        {
            Assert.That(WaitHandle.WaitAll([task1Done, task2Done], TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(logBook, Has.Count.EqualTo(4));
            Assert.That(logBook[0], Is.EqualTo("START #1"));
            Assert.That(logBook[1], Is.EqualTo("END #1"));
            Assert.That(logBook[2], Is.EqualTo("START #2"));
            Assert.That(logBook[3], Is.EqualTo("END #2"));
        });
       
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