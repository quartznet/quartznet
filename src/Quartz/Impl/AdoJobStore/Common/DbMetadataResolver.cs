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

    // Resolving metadata loads several types by name, so the result is remembered. Concurrent because
    // one resolver serves every scheduler in the container.
    private readonly ConcurrentDictionary<string, DbMetadata> resolved = new(StringComparer.Ordinal);

    // The type-free answers are cached apart from the full ones, because they are a different
    // description of the same name and one must never be served where the other was asked for.
    private readonly ConcurrentDictionary<string, DbMetadata> resolvedWithoutTypes = new(StringComparer.Ordinal);

    public DbMetadataResolver(IEnumerable<DbMetadataFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        // Ordering is stable, so factories of equal rank keep their relative registration order.
        // Registration order alone is not enough for two reasons: a second AddQuartz call describing a
        // driver registers after the first call has already pulled the built-ins in, and a description
        // written in code has to beat one spelled as quartz.dbprovider.* keys even when the keys were
        // registered by an earlier call — the same "code beats strings" rule as everywhere else.
        this.factories = factories
            .OrderBy(factory => factory switch
            {
                ConfiguredDbMetadataFactory => 0,
                BuiltInDbMetadataFactory => 2,
                _ => 1
            })
            .ToArray();
    }

    /// <summary>
    /// A resolver over nothing but the driver descriptions Quartz ships, for callers that construct a
    /// <see cref="DbProvider"/> without a container to ask.
    /// </summary>
    /// <remarks>
    /// Shared, and therefore cached, for the lifetime of the process. Safe to share where a container's
    /// resolver is not: it holds only the descriptions Quartz ships, which are the same everywhere, so
    /// there is no container-specific metadata for the cache to leak. A fresh instance per call would
    /// rebuild the description — and re-resolve its driver types — on every <see cref="DbProvider"/>
    /// construction, which is what the deleted static constructor did once.
    /// </remarks>
    public static DbMetadataResolver BuiltIn() => builtIn;

    private static readonly DbMetadataResolver builtIn = new([new BuiltInDbMetadataFactory()]);

    /// <summary>
    /// Returns the metadata describing a provider, or throws naming the providers that are known.
    /// </summary>
    public DbMetadata Resolve(string providerName)
    {
        return Resolve(providerName, resolved, static (factory, name) => factory.GetDbMetadata(name));
    }

    /// <summary>
    /// Returns the metadata describing a provider without the driver's own types, for a provider that
    /// reaches the driver through its <see cref="System.Data.Common.DbProviderFactory" /> or a
    /// <see cref="System.Data.Common.DbDataSource" />.
    /// </summary>
    /// <remarks>
    /// Answers for a driver whose assembly is not loaded at all, which the full resolution cannot: it
    /// names every type it needs and throws when one will not load. That is what makes this the
    /// resolution a trimmed application uses.
    /// </remarks>
    public DbMetadata ResolveWithoutTypes(string providerName)
    {
        return Resolve(providerName, resolvedWithoutTypes, static (factory, name) => factory.GetTypeFreeDbMetadata(name));
    }

    private DbMetadata Resolve(
        string providerName,
        ConcurrentDictionary<string, DbMetadata> cache,
        Func<DbMetadataFactory, string, DbMetadata> describe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        if (cache.TryGetValue(providerName, out var metadata))
        {
            return metadata;
        }

        foreach (var factory in factories)
        {
            if (factory.GetProviderNames().Contains(providerName))
            {
                return cache.GetOrAdd(providerName, describe(factory, providerName));
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
