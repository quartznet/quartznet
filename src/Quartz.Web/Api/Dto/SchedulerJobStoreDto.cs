using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class SchedulerJobStoreDto
    {
        public SchedulerJobStoreDto(SchedulerMetaData metaData)
        {
            Type = metaData.JobStoreType.AssemblyQualifiedNameWithoutVersion();
            Clustered = metaData.JobStoreClustered;
            Persistent = metaData.JobStoreSupportsPersistence;
        }

        public string Type { get; private set; }
        public bool Clustered { get; private set; }
        public bool Persistent { get; private set; }
    }
}