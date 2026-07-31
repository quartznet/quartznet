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
}