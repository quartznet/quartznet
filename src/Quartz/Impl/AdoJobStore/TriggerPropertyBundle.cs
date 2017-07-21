namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Property name and value holder for trigger state data.
    /// </summary>
    public class TriggerPropertyBundle
    {
        public TriggerPropertyBundle(IScheduleBuilder sb, string[] statePropertyNames, object[] statePropertyValues)
        {
            ScheduleBuilder = sb;
            StatePropertyNames = statePropertyNames;
            StatePropertyValues = statePropertyValues;
        }

        public IScheduleBuilder ScheduleBuilder { get; }

        public string[] StatePropertyNames { get; }

        public object[] StatePropertyValues { get; }
    }
}