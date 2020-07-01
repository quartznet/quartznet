using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    public class ServiceCollectionJobConfigurator
    {
        private readonly IServiceCollection collection;
        internal readonly JobBuilder jobBuilder;

        internal ServiceCollectionJobConfigurator(IServiceCollection collection, JobBuilder jobBuilder)
        {
            this.collection = collection;
            this.jobBuilder = jobBuilder;
        }

        public ServiceCollectionJobConfigurator WithIdentity(string name)
        {
            jobBuilder.WithIdentity(name);
            return this;
        }

        public ServiceCollectionJobConfigurator WithIdentity(string name, string @group)
        {
            jobBuilder.WithIdentity(name, @group);
            return this;
        }

        public ServiceCollectionJobConfigurator WithIdentity(JobKey key)
        {
            jobBuilder.WithIdentity(key);
            return this;
        }

        public ServiceCollectionJobConfigurator WithDescription(string? description)
        {
            jobBuilder.WithDescription(description);
            return this;
        }

        public ServiceCollectionJobConfigurator RequestRecovery(bool shouldRecover = true)
        {
            jobBuilder.RequestRecovery(shouldRecover);
            return this;
        }

        public ServiceCollectionJobConfigurator StoreDurably(bool durability = true)
        {
            jobBuilder.StoreDurably(durability);
            return this;
        }

        public JobBuilder SetJobData(JobDataMap? newJobDataMap)
        {
            return jobBuilder.SetJobData(newJobDataMap);
        }
    }
}