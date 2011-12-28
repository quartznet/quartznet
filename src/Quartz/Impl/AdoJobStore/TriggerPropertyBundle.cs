namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Property name and value holder for trigger state data.
    /// </summary>
    public class TriggerPropertyBundle
    {
        private readonly IScheduleBuilder sb;
        private readonly string[] statePropertyNames;
        private readonly object[] statePropertyValues;

        public TriggerPropertyBundle(IScheduleBuilder sb, string[] statePropertyNames, object[] statePropertyValues)
        {
            this.sb = sb;
            this.statePropertyNames = statePropertyNames;
            this.statePropertyValues = statePropertyValues;
        }

        public IScheduleBuilder ScheduleBuilder
        {
            get { return sb; }
        }

        public string[] StatePropertyNames
        {
            get { return statePropertyNames; }
        }

        public object[] StatePropertyValues
        {
            get { return statePropertyValues; }
        }
    }
}