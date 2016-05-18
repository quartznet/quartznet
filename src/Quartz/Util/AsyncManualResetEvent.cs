using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Util
{
    /// <summary>
    /// Inspired by http://blogs.msdn.com/b/pfxteam/archive/2012/02/11/10266920.aspx
    /// </summary>
    internal class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncManualResetEvent(bool set)
        {
            if (set)
            {
                taskCompletionSource.SetResult(true);
            }
        }

        public bool IsSet => taskCompletionSource.Task.IsCompleted;

        public async Task WaitAsync(CancellationToken token = default(CancellationToken))
        {
            if (token.IsCancellationRequested)
            {
                return;
            }
            using (token.Register(() => taskCompletionSource.TrySetCanceled()))
            {
                await taskCompletionSource.Task.ConfigureAwait(false);
            }
        }

        public void Set()
        {
            taskCompletionSource.TrySetResult(true);
        }

        public void Reset()
        {
            var sw = new SpinWait();

            do
            {
                var tcs = taskCompletionSource;
                if (!tcs.Task.IsCompleted ||
#pragma warning disable 420
                    Interlocked.CompareExchange(ref taskCompletionSource, new TaskCompletionSource<bool>(), tcs) == tcs)
#pragma warning restore 420
                    return;

                sw.SpinOnce();
            } while (true);
        }
    }
}