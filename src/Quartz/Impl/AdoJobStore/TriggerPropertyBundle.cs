namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Property name and value holder for trigger state data.
/// </summary>
public sealed class TriggerPropertyBundle
{
    public TriggerPropertyBundle(IScheduleBuilder sb)
        : this(sb, [], [])
    {
    }

    public TriggerPropertyBundle(IScheduleBuilder sb, string[]? statePropertyNames, object[]? statePropertyValues)
    {
        ScheduleBuilder = sb;
        StatePropertyNames = statePropertyNames ?? [];
        StatePropertyValues = statePropertyValues ?? [];

        if (StatePropertyNames.Length != StatePropertyValues.Length)
        {
            ThrowHelper.ThrowArgumentException("property names and values must be of same length");
        }
    }

    public IScheduleBuilder ScheduleBuilder { get; }

    public string[] StatePropertyNames { get; }

    public object[] StatePropertyValues { get; }
}