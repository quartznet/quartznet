namespace Quartz.Configuration;

internal sealed class SchedulerListenerConfiguration
{
    public SchedulerListenerConfiguration(
        Type listenerType,
        string? optionsName = null,
        Func<IServiceProvider, ISchedulerListener>? listenerFactory = null,
        ISchedulerListener? listenerInstance = null)
    {
        ListenerType = listenerType;
        OptionsName = optionsName ?? "";
        ListenerFactory = listenerFactory;
        ListenerInstance = listenerInstance;
    }

    public Type ListenerType { get; }
    public string OptionsName { get; }
    public Func<IServiceProvider, ISchedulerListener>? ListenerFactory { get; }
    public ISchedulerListener? ListenerInstance { get; }
}
