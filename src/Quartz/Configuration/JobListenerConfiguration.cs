namespace Quartz.Configuration;

internal sealed class JobListenerConfiguration(Type listenerType, IMatcher<JobKey>[] matchers)
{
    public Type ListenerType { get; } = listenerType;
    public IMatcher<JobKey>[] Matchers { get; } = matchers;
}