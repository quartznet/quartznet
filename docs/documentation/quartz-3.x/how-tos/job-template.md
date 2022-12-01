---
title: Job Template
---

# Job Template

This page tries to pull together a variety of common recommendations listed throughout the documentation
into one page can be easily referenced.

```csharp
public class SampleJob : IJob
{
    public static readonly JobKey Key = new JobKey("sample-job", "examples");

    public async Task Execute(IJobExecutionContext context)
    {
        try 
        {
            // get data out of the MergedJobDataMap
            var value = context.MergedJobDataMap.GetString("some-value");
            
            // ... do work
        } catch (Exception ex) {
            // do you want the job to refire?
            throw new JobExecutionException(msg: "", refireImmediately: true, cause: ex);
        }
    }
}
```
