using System;
using System.Threading.Tasks;

namespace Quartz.Examples.Example15;

/// <summary>
/// This is just a simple job that gets fired off many times 
/// by ConfigureJobSchedulingByUsingXmlConfigurations Example.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJob : IJob
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with the <see cref="IJob" />.
    /// </summary>
    public virtual Task Execute(IJobExecutionContext context)
    {
        // This job simply prints out its job name and the
        // date and time that it is running
        JobKey jobKey = context.JobDetail.Key;
        Console.WriteLine("SimpleJob says: {0} executing at {1:r}", jobKey, DateTime.Now);
        return Task.CompletedTask;
    }
}