namespace Quartz
{
    public interface IQuartzHostedServiceListener
    {
        bool Running { get; }
        int ErrorCount { get; }
    }
}