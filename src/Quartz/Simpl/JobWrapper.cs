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

        public override bool Equals(object obj)
        {
            JobWrapper jobWrapper = obj as JobWrapper;
            if (jobWrapper != null)
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