namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Serves the metadata of one ADO.NET provider, described in code.
/// </summary>
/// <remarks>
/// This is what a <c>UseGenericDatabase</c> metadata callback becomes: an ordinary registration in the
/// container, so a driver described in code is found the same way as one Quartz ships a description for.
/// </remarks>
internal sealed class ConfiguredDbMetadataFactory : DbMetadataFactory
{
    private readonly string providerName;
    private readonly DbMetadata metadata;

    public ConfiguredDbMetadataFactory(string providerName, DbMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(metadata);

        this.providerName = providerName;
        this.metadata = metadata;
    }

    public override IReadOnlyCollection<string> GetProviderNames() => [providerName];

    public override DbMetadata GetDbMetadata(string providerName)
    {
        if (!string.Equals(providerName, this.providerName, StringComparison.Ordinal))
        {
            Throw.ArgumentException(
                $"This factory describes provider '{this.providerName}', not '{providerName}'.",
                nameof(providerName));
        }

        return metadata;
    }
}
