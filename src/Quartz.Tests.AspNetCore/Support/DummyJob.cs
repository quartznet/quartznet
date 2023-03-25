namespace Quartz.Tests.AspNetCore.Support;

public class DummyJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context) => throw new NotImplementedException();
}