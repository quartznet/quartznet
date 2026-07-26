using System.Collections.Concurrent;
using System.Text;

namespace Quartz.Impl.AdoJobStore.Common;

/// <summary>
/// Resolves ADO.NET provider metadata by provider name, from the metadata factories it is given.
/// </summary>
/// <remarks>
/// <para>
/// One of these per container rather than one per process. Two containers may describe the same provider
/// name differently — that is the whole point of describing a driver in code — so a cache shared between
/// them would hand the second container the first one's metadata. The cache here belongs to the resolver,
/// so it only ever holds what this set of factories produced.
/// </para>
/// <para>
/// Factories are consulted in registration order, except that the descriptions Quartz ships always come
/// last: a driver described in code or by <c>quartz.dbprovider.*</c> keys wins over a built-in of the same
/// name, whichever order the registrations happened to go in.
/// </para>
/// <para>
/// A provider name means one thing per container, since a name is what a data source points at. Two
/// schedulers that need two different drivers give them two different names.
/// </para>
/// </remarks>
internal sealed class DbMetadataResolver
{
    private readonly DbMetadataFactory[] factories;

    // Resolving metadata reads an embedded resource and loads several types by name, so the result is
    // remembered. Concurrent because one resolver serves every scheduler in the container.
    private readonly ConcurrentDictionary<string, DbMetadata> resolved = new(StringComparer.Ordinal);

    public DbMetadataResolver(IEnumerable<DbMetadataFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        // Ordering is stable, so everything the application registered keeps its relative order and only
        // the built-ins are moved to the back. Registration order alone is not enough: a second AddQuartz
        // call describing a driver registers after the first call has already pulled the built-ins in.
        this.factories = factories
            .OrderBy(factory => factory is EmbeddedAssemblyResourceDbMetadataFactory ? 1 : 0)
            .ToArray();
    }

    /// <summary>
    /// A resolver over nothing but the driver descriptions Quartz ships, for callers that construct a
    /// <see cref="DbProvider"/> without a container to ask.
    /// </summary>
    public static DbMetadataResolver BuiltIn() => new([new EmbeddedAssemblyResourceDbMetadataFactory()]);

    /// <summary>
    /// Returns the metadata describing a provider, or throws naming the providers that are known.
    /// </summary>
    public DbMetadata Resolve(string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (resolved.TryGetValue(providerName, out var metadata))
        {
            return metadata;
        }

        foreach (var factory in factories)
        {
            if (factory.GetProviderNames().Contains(providerName))
            {
                return resolved.GetOrAdd(providerName, factory.GetDbMetadata(providerName));
            }
        }

        Throw.ArgumentOutOfRangeException(
            nameof(providerName),
            $"There is no metadata information for provider '{providerName}'{Environment.NewLine}{DescribeKnownProviders()}");

        return default!;
    }

    /// <summary>
    /// Lists the provider names the factories between them know.
    /// </summary>
    public string DescribeKnownProviders()
    {
        var providerNames = factories
            .SelectMany(factory => factory.GetProviderNames())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        var sb = new StringBuilder("Valid DB Provider names are:").Append(Environment.NewLine);
        foreach (var providerName in providerNames)
        {
            sb.Append('\t').Append(providerName).Append(Environment.NewLine);
        }

        return sb.ToString();
    }
}
