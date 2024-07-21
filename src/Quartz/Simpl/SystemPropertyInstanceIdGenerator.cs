using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// InstanceIdGenerator that will use a <see cref="SystemProperty" /> to configure the scheduler.
/// If no value set for the property, a <see cref="SchedulerException" /> is thrown.
/// </summary>
/// <author>Alex Snaps</author>
internal sealed class SystemPropertyInstanceIdGenerator : IInstanceIdGenerator
{
    /// <summary>
    /// System property to read the instanceId from.
    /// </summary>
    public const string SystemProperty = "quartz.scheduler.instanceId";

    private string? prepend;
    private string? postpend;
    private string systemPropertyName = SystemProperty;

    /// <summary>
    /// Returns the cluster wide value for this scheduler instance's id, based on a system property.
    /// </summary>
    public ValueTask<string?> GenerateInstanceId(CancellationToken cancellationToken = default)
    {
        var property = Environment.GetEnvironmentVariable(SystemPropertyName);
        if (property is null)
        {
            ThrowHelper.ThrowSchedulerException("No value for '" + SystemProperty + "' system property found, please configure your environment accordingly!");
        }

        if (Prepend is not null)
        {
            property = Prepend + property;
        }
        if (Postpend is not null)
        {
            property += Postpend;
        }

        return new ValueTask<string?>(property);
    }

    /// <summary>
    /// A string of text to prepend (add to the beginning) to the instanceId found in the system property.
    /// </summary>
    public string? Prepend
    {
        get => prepend;
        set => prepend = value?.Trim();
    }

    /// <summary>
    /// A string of text to postpend (add to the end) to the instanceId found in the system property.
    /// </summary>
    public string? Postpend
    {
        get => postpend;
        set => postpend = value?.Trim();
    }

    /// <summary>
    /// The name of the system property from which to obtain the instanceId.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="SystemProperty"/>.
    /// </remarks>
    public string SystemPropertyName
    {
        get => systemPropertyName;
        set => systemPropertyName = value?.Trim() ?? SystemProperty;
    }
}