---

title: Job Template
---

# Job Template

This page tries to pull together a variety of common recommendations listed throughout the documentation
into one page can be easily referenced.

```csharp
public class SampleJob : IJob
{
    // have a public key that is easy reference in DI configuration for example
    // group helps you with targeting specific jobs in maintenance operations, 
    // like pause all jobs in group "integration"
    public static readonly JobKey Key = new JobKey("sample-job", "examples");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.RefireCount > 10)
        {
            // we might not ever succeed!
            // maybe log a warning, throw another type of error, inform the engineer on call
            return;
        }

        try 
        {
            // get data out of the MergedJobDataMap
            var value = context.MergedJobDataMap.GetString("some-value");
            
            // ... do work - and forward the cancellation token, so an interrupt
            // or a shutdown can actually stop the job
            await Task.Delay(100, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // the scheduler asked the job to stop; let the cancellation flow
            throw;
        }
        catch (Exception ex)
        {
            // do you want the job to refire?
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }
    }
}
```
