using System;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Property name and value holder for trigger state data.
    /// </summary>
    public class TriggerPropertyBundle
    {
        public TriggerPropertyBundle(IScheduleBuilder sb)
            : this(sb, Array.Empty<string>(), Array.Empty<object>())
        {
        }

        public TriggerPropertyBundle(IScheduleBuilder sb, string[]? statePropertyNames, object[]? statePropertyValues)
        {
            ScheduleBuilder = sb;
            StatePropertyNames = statePropertyNames ?? Array.Empty<string>();
            StatePropertyValues = statePropertyValues ?? Array.Empty<object>();

            if (StatePropertyNames.Length != StatePropertyValues.Length)
            {
                throw new ArgumentException("property names and values must be of same length");
            }
        }

        public IScheduleBuilder ScheduleBuilder { get; }

        public string[] StatePropertyNames { get; }

        public object[] StatePropertyValues { get; }
    }
}