namespace Quartz.Tests.AspNetCore.Support;

public class DummyJob : IJob
{
    public Task Execute(IJobExecutionContext context) => throw new NotImplementedException();
}