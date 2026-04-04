namespace Quartz.Configuration;

internal sealed class JobListenerConfiguration
{
    public JobListenerConfiguration(
        Type listenerType,
        IMatcher<JobKey>[] matchers,
        string? optionsName = null,
        Func<IServiceProvider, IJobListener>? listenerFactory = null,
        IJobListener? listenerInstance = null)
    {
        ListenerType = listenerType;
        Matchers = matchers;
        OptionsName = optionsName ?? "";
        ListenerFactory = listenerFactory;
        ListenerInstance = listenerInstance;
    }

    public Type ListenerType { get; }
    public IMatcher<JobKey>[] Matchers { get; }
    public string OptionsName { get; }
    public Func<IServiceProvider, IJobListener>? ListenerFactory { get; }
    public IJobListener? ListenerInstance { get; }
}