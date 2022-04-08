namespace Quartz;

public interface IJobWrapper
{
    IJob Target { get; }
}
