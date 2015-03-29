using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class SchedulerThreadPoolDto
    {
        public SchedulerThreadPoolDto(SchedulerMetaData metaData)
        {
            Type = metaData.ThreadPoolType.AssemblyQualifiedNameWithoutVersion();
            Size = metaData.ThreadPoolSize;
        }

        public string Type { get; private set; }
        public int Size { get; private set; }
    }
}