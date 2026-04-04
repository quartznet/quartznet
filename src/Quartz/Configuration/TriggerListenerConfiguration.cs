namespace Quartz.Configuration;

internal sealed class TriggerListenerConfiguration
{
    public TriggerListenerConfiguration(
        Type listenerType,
        IMatcher<TriggerKey>[] matchers,
        string? optionsName = null,
        Func<IServiceProvider, ITriggerListener>? listenerFactory = null,
        ITriggerListener? listenerInstance = null)
    {
        ListenerType = listenerType;
        Matchers = matchers;
        OptionsName = optionsName ?? "";
        ListenerFactory = listenerFactory;
        ListenerInstance = listenerInstance;
    }

    public Type ListenerType { get; }
    public IMatcher<TriggerKey>[] Matchers { get; }
    public string OptionsName { get; }
    public Func<IServiceProvider, ITriggerListener>? ListenerFactory { get; }
    public ITriggerListener? ListenerInstance { get; }
}