namespace Quartz.Configuration;

internal sealed class TriggerListenerConfiguration(Type listenerType, IMatcher<TriggerKey>[] matchers)
{
    public Type ListenerType { get; } = listenerType;
    public IMatcher<TriggerKey>[] Matchers { get; } = matchers;
}