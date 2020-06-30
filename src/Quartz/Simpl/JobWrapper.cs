namespace Quartz.Simpl
{
    internal class JobWrapper
    {
        public JobKey Key { get; }
        public IJobDetail JobDetail { get; set; }

        internal JobWrapper(IJobDetail jobDetail)
        {
            JobDetail = jobDetail;
            Key = jobDetail.Key;
        }

        public override bool Equals(object? obj)
        {
            if (obj is JobWrapper jobWrapper)
            {
                if (jobWrapper.Key.Equals(Key))
                {
                    return true;
                }
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }
    }
}