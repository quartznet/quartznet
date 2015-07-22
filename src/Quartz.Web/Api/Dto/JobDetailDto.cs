using Quartz.Util;

namespace Quartz.Web.Api.Dto
{
    public class JobDetailDto
    {
        public JobDetailDto(IJobDetail jobDetail)
        {
            Durable = jobDetail.Durable;
            ConcurrentExecutionDisallowed = jobDetail.ConcurrentExecutionDisallowed;
            Description = jobDetail.Description;
            JobType = jobDetail.JobType.AssemblyQualifiedNameWithoutVersion();
            Name = jobDetail.Key.Name;
            Group = jobDetail.Key.Group;
            PersistJobDataAfterExecution = jobDetail.PersistJobDataAfterExecution;
            RequestsRecovery = jobDetail.RequestsRecovery;
        }

        public string Name { get; set; }
        public string Group { get; set; }
        public string JobType { get; set; }
        public string Description { get; set; }

        public bool Durable { get; set; }
        public bool RequestsRecovery { get; set; }
        public bool PersistJobDataAfterExecution { get; set; }
        public bool ConcurrentExecutionDisallowed { get; set; }
    }
}