namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Base class for the DbMetadata Factory implementations
/// </summary>
/// <remarks>
/// Internal: every implementation is internal, no public member accepts or returns one, and a driver is
/// described from the outside through <c>UseGenericDatabase</c>'s metadata callback rather than by
/// registering a factory.
/// </remarks>
internal abstract class DbMetadataFactory
{
    /// <summary>
    /// Gets the supported provider names.
    /// </summary>
    /// <returns>The enumeration of the supported provider names</returns>
    public abstract List<string> GetProviderNames();

    /// <summary>
    /// Gets the database metadata associated to the specified provider name.
    /// </summary>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>The metadata instance for the requested provider</returns>
    public abstract DbMetadata GetDbMetadata(string providerName);

    /// <summary>
    /// Gets the same description without the driver's own types, for a provider that reaches the driver
    /// through its <see cref="System.Data.Common.DbProviderFactory" /> or a
    /// <see cref="System.Data.Common.DbDataSource" /> and so constructs nothing.
    /// </summary>
    /// <remarks>
    /// The default answers with the full description, which is right for every factory that was handed
    /// one: a description written in code or spelled as <c>quartz.dbprovider.*</c> keys already names
    /// whatever types it names, and dropping them would only lose what the application said. The
    /// override that matters is the built-in one, where the types are resolved from a name and asking
    /// for them is what a trimmed application cannot survive.
    /// </remarks>
    /// <param name="providerName">Name of the provider.</param>
    public virtual DbMetadata GetTypeFreeDbMetadata(string providerName) => GetDbMetadata(providerName);
}