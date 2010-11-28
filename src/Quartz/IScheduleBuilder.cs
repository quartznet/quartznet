using Quartz.Spi;

namespace Quartz
{
    public interface IScheduleBuilder
    {
        IMutableTrigger Build();
    }
}