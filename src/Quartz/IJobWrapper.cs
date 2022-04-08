namespace Quartz;

internal interface IJobWrapper
{
    IJob Target { get; }
}
